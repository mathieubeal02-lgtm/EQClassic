// eqbot: headless EverQuest Trilogy test client for the EQClassic servers.
//
//   eqbot login  <login-host> <user> <password> [port]
//   eqbot create <login-host> <user> <password> <character>
//   eqbot play   <login-host> <user> <password> <character>
//   eqbot walk   <login-host> <user> <password> <character> <waypoints file> [seconds]
//   eqbot hunt   <login-host> <user> <password> <character> [seconds]
//
// login:  the login server handshake the real client does, checking every answer:
//         LoginInfo (DES-encrypted credentials) -> SessionId, LoginBanner, ServerList,
//         RequestServerStatus -> SendServerStatus, SessionKey -> session key, AllFinish.
// create: login, world, and character creation from a packet captured from the real client
//         (no-op when the character exists).
// play:   login, world, enter world, then the zone handshake up to "Enterzone complete", and a
//         clean disconnect.
// walk, hunt: player bots (tools/botd, docs/bots-design.md), one decision line per action.
// Exit code 0 when every step succeeded. Output is one line per step, for CI logs.
// EQBOT_VERBOSE=1 lists the zone packets, EQBOT_RAW=1 dumps every datagram received, EQBOT_NETDEBUG=1
// prints the protocol layer's resends and drops and, when the zone goes silent, the last 6000 datagrams.
// EQBOT_STAY=<seconds> keeps `play` in the zone and checks the height of moving mobs.
// EQBOT_CLASS=warrior|cleric|wizard makes `create` build a human of that class instead of a troll shaman.
// EQBOT_INVITE=Name,Name makes a hunter lead a group (invites them); EQBOT_ROLE=member makes it
// never pull: it joins whoever invites it, follows, assists, heals (cleric) or nukes (wizard).
#include <openssl/des.h>
#include <stdint.h>
#include <zlib.h>
#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/select.h>
#include <sys/socket.h>
#include <sys/time.h>
#include <unistd.h>

#include <algorithm>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <set>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#include "EqSession.h"

using namespace EqBot;

namespace
{
	// Login opcodes (LS/Login/login_opcodes.h)
	const int16 kLoginInfo = 0x0100;
	const int16 kFatalError = 0x0200;
	const int16 kSessionId = 0x0400;
	const int16 kAllFinish = 0x0500;
	const int16 kServerList = 0x4600;
	const int16 kSessionKey = 0x4700;
	const int16 kRequestServerStatus = 0x4800;
	const int16 kSendServerStatus = 0x4A00;
	const int16 kLoginBanner = 0x5200;

	const int TIMEOUT_MS = 5000;

	long NowMs()
	{
		timeval tv;
		gettimeofday(&tv, 0);
		return tv.tv_sec * 1000L + tv.tv_usec / 1000;
	}
	int failures = 0;

	void Step(bool ok, const char* name, const std::string& detail)
	{
		printf("[%s] %-22s %s\n", ok ? " OK " : "FAIL", name, detail.c_str());
		fflush(stdout);
		if (!ok)
			failures++;
	}

	// The Trilogy client encrypts {username[20], password[20]} with single DES in CBC mode, the key
	// doubling as IV (LS/Login/EQCrypto.cpp).
	void EncryptCredentials(const std::string& user, const std::string& pass, unsigned char out[40])
	{
		DES_cblock key = { 19, 217, 19, 109, 208, 52, 21, 251 };
		unsigned char plain[40];
		memset(plain, 0, sizeof(plain));
		strncpy((char*)plain, user.c_str(), 19);
		strncpy((char*)plain + 20, pass.c_str(), 19);
		DES_key_schedule schedule;
		DES_set_key_unchecked(&key, &schedule);
		DES_cblock iv;
		memcpy(iv, key, sizeof(iv));
		DES_ncbc_encrypt(plain, out, 40, &schedule, &iv, DES_ENCRYPT);
	}

	// NUL-terminated strings found in a buffer (server list entries are name / IP strings).
	std::vector<std::string> Strings(const unsigned char* data, size_t size, size_t minlen = 3)
	{
		std::vector<std::string> out;
		std::string cur;
		for (size_t i = 0; i < size; i++)
		{
			if (data[i] >= 32 && data[i] < 127)
				cur += (char)data[i];
			else
			{
				if (cur.size() >= minlen)
					out.push_back(cur);
				cur.clear();
			}
		}
		if (cur.size() >= minlen)
			out.push_back(cur);
		return out;
	}

	// Text of a FatalError among `packets` (e.g. "Error 1018: You currently have an active
	// character..."), or "" when there is none.
	std::string FatalText(const std::vector<Packet*>& packets)
	{
		for (size_t i = 0; i < packets.size(); i++)
		{
			if (packets[i]->opcode != kFatalError)
				continue;
			std::vector<std::string> parts = Strings(packets[i]->pBuffer, packets[i]->size);
			std::string text;
			for (size_t j = 0; j < parts.size(); j++)
				text += (j ? " " : "") + parts[j];
			return "FatalError: " + text;
		}
		return "";
	}

	int Login(const std::string& host, int port, const std::string& user, const std::string& pass,
	          std::string* outAccount = 0, std::string* outKey = 0, std::string* outWorld = 0)
	{
		EqSession s;
		if (!s.Open(host, port))
		{
			Step(false, "connect", "cannot resolve " + host);
			return 1;
		}
		Step(true, "connect", s.Peer());

		// 1. Credentials
		unsigned char info[41];
		EncryptCredentials(user, pass, info);
		info[40] = 0;	// "server you last logged out of": none
		s.Send(kLoginInfo, info, sizeof(info));
		std::vector<Packet*> others;
		Packet* p = s.WaitFor(kSessionId, TIMEOUT_MS, &others);
		if (!p)
		{
			std::string why = FatalText(others);
			Step(false, "login", why.empty() ? "no answer" : why);
			DeleteAll(others);
			return 1;
		}
		std::vector<std::string> sid = Strings(p->pBuffer, p->size, 1);
		Step(true, "login", "session id '" + (sid.empty() ? std::string() : sid[0]) + "'");
		if (outAccount && !sid.empty())
			*outAccount = sid[0];
		delete p;
		DeleteAll(others);

		// 2. Banner (optional on the server side, so not a failure when absent)
		s.Send(kLoginBanner, 0, 0);
		p = s.WaitFor(kLoginBanner, 2000);
		if (p)
		{
			std::vector<std::string> b = Strings(p->pBuffer, p->size);
			Step(true, "banner", b.empty() ? "(empty)" : b[0]);
			delete p;
		}
		else
		{
			printf("[ -- ] %-22s %s\n", "banner", "none");
		}

		// 3. Server list
		unsigned char listreq[5] = { 0, 0, 0, 0, 0 };
		s.Send(kServerList, listreq, sizeof(listreq));
		p = s.WaitFor(kServerList, TIMEOUT_MS);
		if (!p || p->size < 4)
		{
			Step(false, "server list", p ? "too short" : "no answer");
			delete p;
			return 1;
		}
		int count = p->pBuffer[0];
		std::vector<std::string> strs = Strings(p->pBuffer + 4, p->size - 4);
		std::string servers, ip;
		for (size_t i = 0; i < strs.size(); i++)
		{
			servers += (i ? ", " : "") + strs[i];
			// the first dotted quad after a server name is its world address
			if (ip.empty() && strs[i].find('.') != std::string::npos && strs[i].find_first_not_of("0123456789.") == std::string::npos)
				ip = strs[i];
		}
		char buf[64];
		snprintf(buf, sizeof(buf), "%d server(s): ", count);
		Step(count > 0 && !ip.empty(), "server list", buf + servers);
		delete p;
		if (ip.empty())
			return 1;

		// 4. Status of the chosen world, then its session key
		std::vector<unsigned char> ipz(ip.begin(), ip.end());
		ipz.push_back(0);
		ipz.push_back(0);
		s.Send(kRequestServerStatus, ipz.data(), ipz.size());
		p = s.WaitFor(kSendServerStatus, TIMEOUT_MS, &others);
		std::string fatal = FatalText(others);
		DeleteAll(others);
		if (!p)
		{
			// The login server refuses here when the account is still marked in game.
			Step(false, "server status", fatal.empty() ? "no answer" : fatal);
			return 1;
		}
		Step(true, "server status", "world " + ip + " up");
		delete p;

		s.Send(kSessionKey, ipz.data(), ipz.size());
		p = s.WaitFor(kSessionKey, TIMEOUT_MS);
		std::string key;
		if (p && p->size > 1)
			key = std::string((const char*)p->pBuffer + 1, strnlen((const char*)p->pBuffer + 1, p->size - 1));
		Step(!key.empty(), "session key", key.empty() ? "none" : "'" + key + "' for world " + ip);
		delete p;

		s.Send(kAllFinish, 0, 0);
		s.Poll(300);
		if (outKey)
			*outKey = key;
		if (outWorld)
			*outWorld = ip;
		return failures ? 1 : 0;
	}

	// World and zone opcodes (Common/Include/eq_opcodes.h)
	const int16 kSendLoginInfo = 0x5818;
	const int16 kSendCharInfo = 0x4720;
	const int16 kEnterWorld = 0x0180;
	const int16 kZoneServerInfo = 0x0480;
	const int16 kZoneUnavail = 0x0580;
	const int16 kSetDataRate = 0xe821;
	const int16 kZoneEntry = 0x2a20;
	const int16 kPlayerProfile = 0x2d20;	// deflated + encrypted, so small on the wire
	const int16 kZoneRequest3 = 0x5d20;	// client -> zone, step 3
	const int16 kZoneRequest4 = 0x0a20;	// client -> zone, step 4 (zone header request)
	const int16 kZoneDone = 0xd820;		// zone -> client after step 4, client -> zone as step 5
	const int WORLD_PORT = 9000;

	const int16 kCharacterCreate = 0x4920;	// PlayerProfile_Struct without its checksum
	const int16 kNameApproval = 0x8b20;		// reserves the name; also World's "creation failed" answer

	// Real client packet, see charcreate_template.inc.
	const unsigned char kCharCreateTemplate[] = {
#include "charcreate_template.inc"
	};

	// Names in a character list (CharacterSelect_Struct starts with char name[10][30]).
	std::vector<std::string> CharacterNames(const Packet* p)
	{
		std::vector<std::string> names;
		for (int i = 0; i < 10 && (size_t)(i + 1) * 30 <= (size_t)p->size; i++)
		{
			std::string n((const char*)p->pBuffer + i * 30, strnlen((const char*)p->pBuffer + i * 30, 30));
			if (!n.empty() && n != "<none>")
				names.push_back(n);
		}
		return names;
	}

	std::string Join(const std::vector<std::string>& v)
	{
		std::string s;
		for (size_t i = 0; i < v.size(); i++)
			s += (i ? ", " : "") + v[i];
		return s.empty() ? "(empty)" : s;
	}

	// Login server, then world: returns the character names, or false on failure.
	// The login server's session ticket of each account: changing zones, a client goes back to World with
	// it (no new login), as the real client does.
	struct WorldTicket { std::string account, key, world; };
	std::map<std::string, WorldTicket> worldTickets;
	bool reuseWorldTicket = false;	// set while zoning

	bool EnterWorldServer(EqSession& w, const std::string& host, const std::string& user, const std::string& pass,
	                      std::vector<std::string>* names)
	{
		std::string account, key, world;
		if (reuseWorldTicket && worldTickets.count(user))
		{
			account = worldTickets[user].account;
			key = worldTickets[user].key;
			world = worldTickets[user].world;
			Step(true, "world ticket", "kept from the login: " + account);
		}
		else
		{
			if (Login(host, 5999, user, pass, &account, &key, &world) != 0)
				return false;
			worldTickets[user] = WorldTicket{ account, key, world };
		}
		w.Open(world, WORLD_PORT);
		std::vector<unsigned char> li(account.begin(), account.end());
		li.push_back(0);
		li.insert(li.end(), key.begin(), key.end());
		li.push_back(0);
		li.resize(li.size() + 16, 0);
		w.Send(kSendLoginInfo, li.data(), li.size());
		Packet* p = w.WaitFor(kSendCharInfo, TIMEOUT_MS);
		if (!p)
		{
			Step(false, "world login", "no character list from " + w.Peer());
			return false;
		}
		*names = CharacterNames(p);
		delete p;
		return true;
	}

	// Creates `charname` on the account from the captured client packet, unless it exists.
	int Create(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname)
	{
		EqSession w;
		std::vector<std::string> names;
		if (!EnterWorldServer(w, host, user, pass, &names))
			return 1;
		for (size_t i = 0; i < names.size(); i++)
			if (names[i] == charname)
			{
				Step(true, "character list", Join(names) + " (already there)");
				w.Disconnect();
				return failures ? 1 : 0;
			}
		Step(true, "character list", Join(names));

		// The client first has the name approved: World reserves a character_ row, which creation
		// then fills. NameApproval_Struct: char name[30], then race at 32 and class at 36 (only
		// logged by World; the template is a troll shaman, race 9 class 10).
		unsigned char approval[40];
		memset(approval, 0, sizeof(approval));
		strncpy((char*)approval, charname.c_str(), 29);
		// The template is a troll shaman. EQBOT_CLASS=warrior, cleric or wizard makes a human of that class
		// (welcome in Qeynos): base stats from World's CheckCharCreateInfo, the free points in STR or WIS.
		std::string cls = getenv("EQBOT_CLASS") ? getenv("EQBOT_CLASS") : "";
		int race = 9, klass = 10;
		const unsigned char* stats = 0;	// STR STA CHA DEX INT AGI WIS
		static const unsigned char warriorStats[7] = { 110, 85, 75, 75, 75, 80, 75 };
		static const unsigned char clericStats[7] = { 80, 80, 75, 75, 75, 75, 115 };
		static const unsigned char wizardStats[7] = { 75, 85, 75, 75, 115, 75, 75 };
		if (cls == "warrior") { race = 1; klass = 1; stats = warriorStats; }
		else if (cls == "cleric") { race = 1; klass = 2; stats = clericStats; }
		else if (cls == "wizard") { race = 1; klass = 12; stats = wizardStats; }
		else if (!cls.empty()) { Step(false, "class", cls + ": only warrior, cleric or wizard (default: troll shaman)"); return 1; }
		approval[32] = race;
		approval[36] = klass;
		w.Send(kNameApproval, approval, sizeof(approval));
		Packet* p = w.WaitFor(kNameApproval, TIMEOUT_MS);
		bool approved = p && p->size >= 1 && p->pBuffer[0] == 1;
		Step(approved, "name approval", p ? (approved ? charname : "name refused") : "no answer");
		delete p;
		if (!approved)
			return 1;

		std::vector<unsigned char> cc(kCharCreateTemplate, kCharCreateTemplate + sizeof(kCharCreateTemplate));
		strncpy((char*)cc.data(), charname.c_str(), 29);
		if (stats)
		{
			// the template is the profile without its 4-byte checksum: race at 52, class at 54, stats at 119,
			// deity at 4152 (Karana for a Qeynos human)
			cc[52] = race;
			cc[54] = klass;
			memcpy(&cc[119], stats, 7);
			cc[4152] = 207 & 0xff;
			cc[4153] = 207 >> 8;
		}
		w.Send(kCharacterCreate, cc.data(), cc.size());
		std::vector<Packet*> others;
		p = w.WaitFor(kSendCharInfo, 10000, &others);
		bool refused = false;
		for (size_t i = 0; i < others.size(); i++)
			refused = refused || others[i]->opcode == kNameApproval;
		DeleteAll(others);
		bool ok = false;
		if (p)
		{
			names = CharacterNames(p);
			for (size_t i = 0; i < names.size(); i++)
				ok = ok || names[i] == charname;
		}
		Step(ok, "create character", p ? Join(names) : refused ? "refused by World" : "no answer");
		delete p;
		w.Disconnect();
		return failures ? 1 : 0;
	}

	const int16 kMobUpdate = 0xa120;	// SpawnPositionUpdates_Struct: int32 count, then 15-byte updates

	// Stays `seconds` in the zone and reports, per mob, how its height moved between position
	// updates: a walking NPC that jumps tens of units up and down is landing on roofs.
	void WatchMobs(EqSession& z, int seconds)
	{
		struct Track { int updates; float minZ, maxZ, lastZ, maxJump; };
		std::map<int, Track> mobs;
		long end = NowMs() + seconds * 1000L;
		while (NowMs() < end)
		{
			std::vector<Packet*> got = z.Poll(500);
			for (size_t i = 0; i < got.size(); i++)
			{
				Packet* p = got[i];
				if (getenv("EQBOT_VERBOSE") && p->opcode != kMobUpdate)
					printf("       stay <- 0x%04x %4d bytes  %s\n", (unsigned)(unsigned short)p->opcode, (int)p->size, HexDump(p->pBuffer, p->size, 48).c_str());
				if (p->opcode != kMobUpdate || p->size < 4)
					continue;
				int n = p->pBuffer[0] | (p->pBuffer[1] << 8);
				for (int k = 0; k < n && 4 + (k + 1) * 15 <= (int)p->size; k++)
				{
					const unsigned char* u = p->pBuffer + 4 + k * 15;
					int id = u[0] | (u[1] << 8);
					float zz = (short)(u[9] | (u[10] << 8)) / 10.0f;	// NPC z is sent x10
					if (getenv("EQBOT_TRACE") && atoi(getenv("EQBOT_TRACE")) == id)
						printf("       trace %d: x %d y %d z %.1f\n", id, (short)(u[7] | (u[8] << 8)), (short)(u[5] | (u[6] << 8)), zz);
					Track& t = mobs[id];
					if (t.updates == 0)
					{
						t.minZ = t.maxZ = t.lastZ = zz;
						t.maxJump = 0;
					}
					float jump = zz > t.lastZ ? zz - t.lastZ : t.lastZ - zz;
					if (jump > t.maxJump)
						t.maxJump = jump;
					if (zz < t.minZ) t.minZ = zz;
					if (zz > t.maxZ) t.maxZ = zz;
					t.lastZ = zz;
					t.updates++;
				}
			}
			DeleteAll(got);
		}
		int jumpy = 0;
		for (std::map<int, Track>::iterator it = mobs.begin(); it != mobs.end(); ++it)
		{
			const Track& t = it->second;
			if (t.updates < 2)
				continue;
			printf("       mob %5d  %3d updates  z %7.1f .. %7.1f  largest step %6.1f\n", it->first, t.updates, t.minZ, t.maxZ, t.maxJump);
			if (t.maxJump > 20)
				jumpy++;
		}
		char b[96];
		snprintf(b, sizeof(b), "%d moving mob(s), %d with a height jump over 20 units", (int)mobs.size(), jumpy);
		Step(jumpy == 0, "mob heights", b);
	}

	// What the zone handshake gave: the zone's name, the player profile and the spawn packets, as
	// received (still deflated and encrypted).
	struct ZoneState
	{
		std::string zone;
		std::vector<unsigned char> profile;
		std::vector<std::vector<unsigned char> > spawnPackets;
		// NPCs spawned one by one during the zone-in (a zone that just booted for us)
		std::vector<std::vector<unsigned char> > newSpawnPackets;
		int myId;	// our spawn id (OP_SpawnAppearance type 16 at zone-in)
		std::vector<unsigned char> inventory;	// OP_CPlayerItems (0xf621): int16 count, deflated {opcode, Item_Struct}[]
		ZoneState() : myId(0) {}
	};

	// Keeps the spawn packets of a batch: lists, and single NPCs (NewSpawn, 0x4921).
	void KeepSpawnPackets(ZoneState& st, const std::vector<Packet*>& got)
	{
		for (size_t i = 0; i < got.size(); i++)
		{
			std::vector<unsigned char> d(got[i]->pBuffer, got[i]->pBuffer + got[i]->size);
			if (got[i]->opcode == 0x6121)
				st.spawnPackets.push_back(d);
			else if (got[i]->opcode == 0x4921 && got[i]->size >= 168)
				st.newSpawnPackets.push_back(d);
			else if (got[i]->opcode == (int16)0xf520 && got[i]->size >= 12 && (d[4] | (d[5] << 8)) == 16)
				st.myId = d[8] | (d[9] << 8);
			else if (got[i]->opcode == (int16)0xf621)
				st.inventory = d;
		}
	}

	const int16 kZoneSpawns = 0x6121;	// NewSpawn_Struct[], deflated + encrypted

	// login -> world -> character select -> enter world -> zone handshake. Leaves `z` open in the zone.
	bool EnterZone(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname,
	               EqSession& z, ZoneState& st)
	{
		EqSession w;
		std::vector<std::string> names;
		if (!EnterWorldServer(w, host, user, pass, &names))
			return false;
		bool found = false;
		for (size_t i = 0; i < names.size(); i++)
			found = found || names[i] == charname;
		Step(found, "character list", Join(names));
		if (!found)
			return false;
		std::vector<Packet*> others;
		Packet* p;

		char name30[30];
		memset(name30, 0, sizeof(name30));
		strncpy(name30, charname.c_str(), sizeof(name30) - 1);
		w.Send(kEnterWorld, name30, sizeof(name30));
		p = w.WaitFor(kZoneServerInfo, 90000, &others);
		bool unavail = false;
		for (size_t i = 0; i < others.size(); i++)
			if (others[i]->opcode == kZoneUnavail)
				unavail = true;
		DeleteAll(others);
		if (!p || p->size < 130)
		{
			Step(false, "enter world", unavail ? "zone unavailable" : "no zone server info");
			delete p;
			return false;
		}
		std::string zip((const char*)p->pBuffer, strnlen((const char*)p->pBuffer, 75));
		std::string zname((const char*)p->pBuffer + 75, strnlen((const char*)p->pBuffer + 75, 53));
		int zport = (p->pBuffer[128] << 8) | p->pBuffer[129];
		char zb[128];
		snprintf(zb, sizeof(zb), "%s at %s:%d", zname.c_str(), zip.c_str(), zport);
		Step(true, "enter world", zb);
		delete p;

		// ---- zone ----
		st.zone = zname;
		z.Open(zip, zport);
		float rate = 5.0f;
		z.Send(kSetDataRate, &rate, sizeof(rate));
		unsigned char entry[36];
		memset(entry, 0, sizeof(entry));
		strncpy((char*)entry + 4, charname.c_str(), 30);
		z.Send(kZoneEntry, entry, sizeof(entry));
		// The zone answers with the player profile (deflated and encrypted), then weather and
		// spell gems. Position updates of nearby mobs may come first.
		std::vector<Packet*> got;
		long t0 = NowMs();
		p = z.WaitFor(kPlayerProfile, 30000, &got);
		long profileMs = NowMs() - t0;
		std::vector<Packet*> more = z.Poll(1000);
		got.insert(got.end(), more.begin(), more.end());
		int dupProfiles = 0;
		for (size_t i = 0; i < got.size(); i++)
		{
			if (got[i]->opcode == kPlayerProfile)
				dupProfiles++;
			if (getenv("EQBOT_VERBOSE"))
				printf("       zone -> 0x%04x %d bytes\n", (unsigned)(unsigned short)got[i]->opcode, (int)got[i]->size);
		}
		char gb[128];
		if (p)
			snprintf(gb, sizeof(gb), "player profile %d bytes after %ld ms, %d other packets", (int)p->size, profileMs, (int)got.size());
		// A second profile means the zone resent its packets: our acks did not reach it.
		Step(p != 0 && dupProfiles == 0, "zone entry", !p ? "no player profile" : dupProfiles ? "player profile resent by the zone" : gb);
		if (p)
			st.profile.assign(p->pBuffer, p->pBuffer + p->size);
		delete p;
		KeepSpawnPackets(st, got);	// the inventory comes right after the profile
		DeleteAll(got);

		z.Send(kZoneRequest3, name30, sizeof(name30));
		{
			std::vector<Packet*> early = z.Poll(1000);
			for (size_t i = 0; i < early.size(); i++)
			{
				if (getenv("EQBOT_VERBOSE"))
					printf("       step3 -> 0x%04x %d bytes\n", (unsigned)(unsigned short)early[i]->opcode, (int)early[i]->size);
			}
			KeepSpawnPackets(st, early);
			DeleteAll(early);
		}
		z.Send(kZoneRequest4, 0, 0);
		p = z.WaitFor(kZoneDone, 20000, &others);
		int spawns = (int)others.size();
		KeepSpawnPackets(st, others);
		DeleteAll(others);
		snprintf(gb, sizeof(gb), "zone header + %d spawn/door/object packets", spawns);
		Step(p != 0, "zone data", p ? gb : "no answer");
		delete p;
		z.Send(kZoneDone, 0, 0);
		// The spawn list may come after the handshake (a zone that just booted is slow): keep collecting
		// until 1.5 s pass without a new spawn packet, 8 s at most.
		long until = NowMs() + 8000, quietSince = NowMs();
		size_t known = st.spawnPackets.size();
		while (NowMs() < until && (st.spawnPackets.empty() || NowMs() - quietSince < 1500))
		{
			if (st.spawnPackets.size() != known)
			{
				known = st.spawnPackets.size();
				quietSince = NowMs();
			}
			std::vector<Packet*> late = z.Poll(200);
			for (size_t i = 0; i < late.size(); i++)
			{
				if (getenv("EQBOT_VERBOSE"))
					printf("       late -> 0x%04x %d bytes\n", (unsigned)(unsigned short)late[i]->opcode, (int)late[i]->size);
			}
			KeepSpawnPackets(st, late);
			DeleteAll(late);
		}
		Step(true, "in zone", charname + " in " + zname);
		return true;
	}

	int Play(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname)
	{
		EqSession z;
		ZoneState st;
		if (!EnterZone(host, user, pass, charname, z, st))
			return 1;
		if (getenv("EQBOT_STAY"))
			WatchMobs(z, atoi(getenv("EQBOT_STAY")));
		z.Disconnect();
		return failures ? 1 : 0;
	}


	// ---- test: in-zone scenarios ----

	const int16 kChannelMessage = 0x0721;	// ChannelMessage_Struct: 70-byte header (language at 64, channel at 66), then the text
	const int16 kSpecialMesg = 0x8021;		// SpecialMesg_Struct: int32 type, then the text
	const int16 kSummonedItem = 0x7821;		// Item_Struct: item_nr at 130
	const int16 kMoveItem = 0x2c21;			// MoveItem_Struct: from, to, number_in_stack (uint32 each)
	const int16 kClientTarget = 0x6221;		// ClientTarget_Struct: int16 target, int16 unused
	const int16 kClientUpdate = 0xf320;		// SpawnPositionUpdate_Struct (15 bytes)
	const int16 kAutoAttack = 0x5121;		// uint32: 1 on, 0 off
	const int16 kDeath = 0x4a20;			// Death_Struct: spawn id, killer id, ..., attack skill at 14
	const int16 kAction = 0x5820;			// Action_Struct: a hit (target, source, type = attack skill, damage)
	const int SaySlot = 8;					// MessageChannel_SAY
	const uint32_t kCursor = 0, kDestroy = 0xFFFFFFFF;

	uint32_t Rotr(uint32_t v, int n) { return (v >> n) | (v << (32 - n)); }

	// The heading byte (0-255) that faces a target, by the zone's own test (Mob::CanNotSeeTarget:
	// x inverted, heading turned by 90 degrees): the one with the smallest angle to it.
	uint8_t HeadingTowards(float x, float y, float tx, float ty)
	{
		uint8_t best = 0;
		float bestAngle = 1e9f;
		for (int h = 0; h < 256; h++)
		{
			float heading = (float)(int8_t)h * 360.0f / 256.0f;
			heading = heading < 270 ? heading + 90 : heading - 270;
			heading = heading * 3.1415f / 180.0f;
			float px = -x, py = y;
			float vx = 10.0f * cosf(heading), vy = 10.0f * sinf(heading);
			float mx = -tx - px, my = ty - py;
			float len = sqrtf(mx * mx + my * my);
			if (len < 0.01f)
				return 0;
			float angle = acosf((vx * mx + vy * my) / (10.0f * len));
			if (angle < bestAngle)
			{
				bestAngle = angle;
				best = (uint8_t)h;
			}
		}
		return best;
	}

	// Undoes the servers' packet encryption (Common/Source/packet_functions.cpp): each word was
	// rotated, offset and chained with the previous one, after swapping word 0 with the middle word.
	void Decrypt(std::vector<unsigned char>& d, uint32_t crypt, uint32_t offset, int rot1, int rot2, bool swapped = true)
	{
		uint32_t* w = (uint32_t*)&d[0];
		size_t words = d.size() / 4;
		for (size_t i = 0; i < words; i++)
		{
			uint32_t t = Rotr(w[i] + crypt, rot2) - offset;
			uint32_t orig = Rotr(t, rot1);
			crypt = crypt + orig - offset;
			w[i] = orig;
		}
		if (swapped && d.size() / 8 < words)
		{
			uint32_t swap = w[0];
			w[0] = w[d.size() / 8];
			w[d.size() / 8] = swap;
		}
	}

	std::vector<unsigned char> Inflate(const std::vector<unsigned char>& in, size_t max)
	{
		std::vector<unsigned char> out(max);
		uLongf len = max;
		if (in.empty() || uncompress(&out[0], &len, &in[0], in.size()) != Z_OK)
			return std::vector<unsigned char>();
		out.resize(len);
		return out;
	}

	// The player profile as the zone keeps it (EncryptProfilePacket + DeflatePacket).
	std::vector<unsigned char> DecodeProfile(std::vector<unsigned char> d)
	{
		if (d.size() < 8)
			return std::vector<unsigned char>();
		Decrypt(d, 0x65e7, 0x37a9, 7, 15);
		return Inflate(d, 20000);
	}

	struct SpawnInfo { int id; std::string name; int npc; int level; int cls; float x, y, z, size; };

	// NewSpawn_Struct[] of the zone's spawn packets (EncryptZoneSpawnPacket + DeflatePacket):
	// 168 bytes each, the Spawn_Struct after a 4-byte placeholder.
	SpawnInfo ParseSpawn(const unsigned char* sp)
	{
		SpawnInfo si;
		si.id = sp[62] | (sp[63] << 8);
		si.name = std::string((const char*)sp + 100, strnlen((const char*)sp + 100, 30));
		si.npc = sp[73];
		si.level = sp[76];
		si.cls = sp[74];
		memcpy(&si.size, sp, 4);	// Spawn_Struct: float size at 0 (0: the race's)
		si.y = (short)(sp[51] | (sp[52] << 8));
		si.x = (short)(sp[53] | (sp[54] << 8));
		// z on the wire: NPCs x10, players x1000 (Mob::FillSpawnStruct); kept in real units here
		si.z = (short)(sp[55] | (sp[56] << 8)) / (si.npc == 1 ? 10.0f : 1000.0f);
		return si;
	}

	std::vector<SpawnInfo> DecodeSpawns(const std::vector<std::vector<unsigned char> >& packets);

	const int16 kNewSpawn = 0x4921;	// one NewSpawn_Struct (168 bytes), EncryptSpawnPacket: not deflated, no swap

	// Spawns known in the zone: the list given on entry, then every NPC spawned later (a zone that just
	// booted fills up after the player is in).
	std::vector<SpawnInfo>* g_spawns = 0;

	void AbsorbSpawns(const std::vector<Packet*>& got)
	{
		if (!g_spawns)
			return;
		for (size_t i = 0; i < got.size(); i++)
		{
			if (getenv("EQBOT_VERBOSE") && got[i]->opcode != 0xa120)
				printf("       test <- 0x%04x %d bytes\n", (unsigned)(unsigned short)got[i]->opcode, (int)got[i]->size);
			if (got[i]->opcode == kZoneSpawns)
			{
				// A later spawn list (a zone still loading sends its NPCs this way too).
				std::vector<std::vector<unsigned char> > one(1, std::vector<unsigned char>(got[i]->pBuffer, got[i]->pBuffer + got[i]->size));
				std::vector<SpawnInfo> more = DecodeSpawns(one);
				g_spawns->insert(g_spawns->end(), more.begin(), more.end());
				continue;
			}
			if (got[i]->opcode != kNewSpawn || got[i]->size < 168)
				continue;
			std::vector<unsigned char> d(got[i]->pBuffer, got[i]->pBuffer + got[i]->size);
			Decrypt(d, 0, 0x65e7, 9, 13, false);
			g_spawns->push_back(ParseSpawn(&d[4]));
		}
	}

	std::vector<SpawnInfo> DecodeSpawns(const std::vector<std::vector<unsigned char> >& packets)
	{
		std::vector<SpawnInfo> out;
		for (size_t k = 0; k < packets.size(); k++)
		{
			std::vector<unsigned char> d = packets[k];
			if (d.size() < 8)
				continue;
			Decrypt(d, 0, 0x65e7, 9, 13);
			std::vector<unsigned char> raw = Inflate(d, 400000);
			for (size_t o = 0; o + 168 <= raw.size(); o += 168)
			{
				out.push_back(ParseSpawn(&raw[o + 4]));
			}
		}
		return out;
	}

	void Say(EqSession& z, const std::string& me, const std::string& text)
	{
		std::vector<unsigned char> b(70 + text.size() + 1, 0);
		strncpy((char*)&b[32], me.c_str(), 22);
		b[66] = SaySlot;	// chan_num (int16 at 66; language at 64 is 0, common tongue)
		memcpy(&b[70], text.c_str(), text.size());
		z.Send(kChannelMessage, &b[0], b.size());
	}

	void MoveItem(EqSession& z, uint32_t from, uint32_t to)
	{
		uint32_t mi[3] = { from, to, 0 };
		z.Send(kMoveItem, mi, sizeof(mi));
	}

	// Texts of the server's messages (SpecialMesg) and says (ChannelMessage: "sender: text") among packets.
	std::vector<std::string> Texts(const std::vector<Packet*>& got)
	{
		std::vector<std::string> out;
		for (size_t i = 0; i < got.size(); i++)
		{
			const Packet* p = got[i];
			if (p->opcode == kSpecialMesg && p->size > 4)
				out.push_back(std::string((const char*)p->pBuffer + 4, strnlen((const char*)p->pBuffer + 4, p->size - 4)));
			else if (p->opcode == kChannelMessage && p->size > 70)
				out.push_back(std::string((const char*)p->pBuffer + 32, strnlen((const char*)p->pBuffer + 32, 23)) + ": "
					+ std::string((const char*)p->pBuffer + 70, strnlen((const char*)p->pBuffer + 70, p->size - 70)));
		}
		return out;
	}

	// Packets for `ms` (or until `done` says so), all kept.
	template <typename Done>
	std::vector<Packet*> Collect(EqSession& z, int ms, Done done)
	{
		std::vector<Packet*> all;
		long end = NowMs() + ms;
		while (NowMs() < end)
		{
			std::vector<Packet*> got = z.Poll(200);
			AbsorbSpawns(got);
			all.insert(all.end(), got.begin(), got.end());
			if (done(all))
				break;
		}
		return all;
	}

	bool HasText(const std::vector<Packet*>& got, const std::string& part, std::string* found = 0)
	{
		std::vector<std::string> t = Texts(got);
		for (size_t i = 0; i < t.size(); i++)
			if (t[i].find(part) != std::string::npos)
			{
				if (found) *found = t[i];
				return true;
			}
		return false;
	}

	int SummonedItemId(const std::vector<Packet*>& got)
	{
		for (size_t i = 0; i < got.size(); i++)
			if (got[i]->opcode == kSummonedItem && got[i]->size > 132)
				return got[i]->pBuffer[130] | (got[i]->pBuffer[131] << 8);
		return 0;
	}

	std::string LastTexts(const std::vector<Packet*>& got, size_t n = 2)
	{
		std::vector<std::string> t = Texts(got);
		std::string out;
		for (size_t i = t.size() > n ? t.size() - n : 0; i < t.size(); i++)
			out += (out.empty() ? "" : " | ") + t[i];
		return out.empty() ? "no message" : out;
	}

	// eqbot test: in-zone scenarios against the legacy servers, one [ OK ]/[FAIL] line each.
	int Test(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname)
	{
		EqSession z;
		ZoneState st;
		if (!EnterZone(host, user, pass, charname, z, st))
			return 1;
		const uint16_t kMuffin = 13014;

		// The profile: this character, and a free general slot (22-29) for the item tests.
		std::vector<unsigned char> pp = DecodeProfile(st.profile);
		size_t at = std::string(pp.begin(), pp.end()).find(charname);
		int freeSlot = -1;
		if (at != std::string::npos && at >= 4 && at - 4 + 168 + 60 <= pp.size())
		{
			const unsigned char* inv = &pp[at - 4 + 168];	// PlayerProfile_Struct: inventory[30] at 168 (name at 4)
			for (int slot = 29; slot >= 22; slot--)
				if ((inv[slot * 2] | (inv[slot * 2 + 1] << 8)) == 0xFFFF)
					freeSlot = slot;
		}
		char b[160];
		snprintf(b, sizeof(b), "%d bytes, %s, first free general slot %d", (int)pp.size(), at != std::string::npos ? "name found" : "name not found", freeSlot);
		Step(at != std::string::npos && freeSlot >= 0, "profile", b);

		// The spawns: this player among them, and NPCs.
		std::vector<SpawnInfo> spawns = DecodeSpawns(st.spawnPackets);
		for (size_t i = 0; i < st.newSpawnPackets.size(); i++)
		{
			std::vector<unsigned char> d = st.newSpawnPackets[i];
			Decrypt(d, 0, 0x65e7, 9, 13, false);
			spawns.push_back(ParseSpawn(&d[4]));
		}
		g_spawns = &spawns;
		// A zone that just booted spawns its NPCs after the player came in: wait for them.
		std::vector<Packet*> waited = Collect(z, 15000, [&](const std::vector<Packet*>&) {
			for (size_t i = 0; i < spawns.size(); i++) if (spawns[i].npc == 1) return true;
			return false; });
		DeleteAll(waited);
		int npcs = 0, meId = 0;
		for (size_t i = 0; i < spawns.size(); i++)
		{
			if (spawns[i].npc == 1) npcs++;
			if (spawns[i].name == charname) meId = spawns[i].id;
			if (getenv("EQBOT_VERBOSE") && spawns[i].npc != 1)
				printf("       spawn #%d '%s' npc %d level %d at %.0f, %.0f, %.0f\n", spawns[i].id, spawns[i].name.c_str(), spawns[i].npc, spawns[i].level, spawns[i].x, spawns[i].y, spawns[i].z);
		}
		// (The player's own spawn is not in the zone's list: the client builds it from the profile.)
		snprintf(b, sizeof(b), "%d spawns, %d NPCs", (int)spawns.size(), npcs);
		Step(npcs > 0, "spawn list", b);

		// A GM command answers (# commands go through the say channel).
		Say(z, charname, "#loc");
		// A zone that just booted first sends the ~100 NPCs of its bulk phase: at the client's data
		// rate the answer can take well over 10 s to come through.
		std::vector<Packet*> got = Collect(z, 30000, [](const std::vector<Packet*>& g) { return HasText(g, "Location"); });
		std::string text;
		bool located = HasText(got, "Location", &text);
		Step(located, "GM command #loc", located ? text : LastTexts(got));
		DeleteAll(got);

		// #si puts an item on the cursor.
		Say(z, charname, "#clearcursor");
		DeleteAll(got = Collect(z, 1500, [](const std::vector<Packet*>&) { return false; }));
		Say(z, charname, "#si 13014");
		got = Collect(z, 10000, [](const std::vector<Packet*>& g) { return SummonedItemId(g) != 0; });
		int item = SummonedItemId(got);
		snprintf(b, sizeof(b), "item %d on the cursor", item);
		Step(item == kMuffin, "summon an item", item ? b : LastTexts(got));
		DeleteAll(got);

		// Put down into a free slot, then #si again: the cursor must be free (fix-cursor-duplicate:
		// the server used to keep a copy of what was put down, and refused to summon).
		if (freeSlot >= 0)
		{
			MoveItem(z, kCursor, (uint32_t)freeSlot);
			DeleteAll(got = Collect(z, 1500, [](const std::vector<Packet*>&) { return false; }));
			Say(z, charname, "#si 13014");
			got = Collect(z, 10000, [](const std::vector<Packet*>& g) { return SummonedItemId(g) != 0 || HasText(g, "cursor is not empty"); });
			bool again = SummonedItemId(got) == kMuffin;
			Step(again, "put down, summon again", again ? "the cursor was free again" : LastTexts(got));
			DeleteAll(got);
			// Tidy up: both muffins destroyed.
			MoveItem(z, kCursor, kDestroy);
			z.Poll(500);
			MoveItem(z, (uint32_t)freeSlot, kCursor);
			z.Poll(500);
			MoveItem(z, kCursor, kDestroy);
			DeleteAll(got = Collect(z, 1000, [](const std::vector<Packet*>&) { return false; }));
		}

		// Melee with a weapon: its hits must come as slashes, not punches (a paladin's Fiery Avenger read
		// "You punch"). The weapon (EQBOT_WEAPON, default 11050 Fiery Avenger, 2H slashing) goes to the
		// primary hand (slot 13); the bot stands next to a low level NPC and auto-attacks it.
		{
			int weapon = getenv("EQBOT_WEAPON") ? atoi(getenv("EQBOT_WEAPON")) : 11050;
			// The prey: the closest small NPC that stands still. Watch 3 s of position updates first: a
			// walking, flying or swimming one is hit from where it was (\"too far away\").
			std::set<int> moving;
			{
				std::map<int, std::pair<float, float> > seen;
				std::vector<Packet*> watch = Collect(z, 3000, [](const std::vector<Packet*>&) { return false; });
				for (size_t i = 0; i < watch.size(); i++)
				{
					const Packet* p = watch[i];
					if (p->opcode != kMobUpdate || p->size < 4)
						continue;
					int n = p->pBuffer[0] | (p->pBuffer[1] << 8);
					for (int k = 0; k < n && 4 + (k + 1) * 15 <= (int)p->size; k++)
					{
						const unsigned char* m = p->pBuffer + 4 + k * 15;
						int id = m[0] | (m[1] << 8);
						float uy = (short)(m[5] | (m[6] << 8)), ux = (short)(m[7] | (m[8] << 8));
						if (seen.count(id) && (std::fabs(seen[id].first - ux) > 1 || std::fabs(seen[id].second - uy) > 1))
							moving.insert(id);
						seen[id] = std::make_pair(ux, uy);
						for (size_t s2 = 0; s2 < spawns.size(); s2++)
							if (spawns[s2].id == id && (std::fabs(spawns[s2].x - ux) > 2 || std::fabs(spawns[s2].y - uy) > 2))
								moving.insert(id);
					}
				}
				DeleteAll(watch);
			}
			float meX = 0, meY = 0;
			if (pp.size() >= 2416)
			{
				memcpy(&meY, &pp[2408], 4);
				memcpy(&meX, &pp[2412], 4);
			}
			const SpawnInfo* prey = 0;
			float best = 1e30f;
			for (size_t i = 0; i < spawns.size(); i++)
			{
				if (spawns[i].npc != 1 || spawns[i].level > 3 || spawns[i].name.compare(0, 2, "a_") != 0 || moving.count(spawns[i].id))
					continue;
				float d = (spawns[i].x - meX) * (spawns[i].x - meX) + (spawns[i].y - meY) * (spawns[i].y - meY);
				if (d < best)
				{
					best = d;
					prey = &spawns[i];
				}
			}
			Say(z, charname, "#clearcursor");
			DeleteAll(got = Collect(z, 1500, [](const std::vector<Packet*>&) { return false; }));
			Say(z, charname, "#si " + std::to_string(weapon));
			got = Collect(z, 10000, [](const std::vector<Packet*>& g) { return SummonedItemId(g) != 0; });
			bool summoned = SummonedItemId(got) == weapon;
			DeleteAll(got);
			if (!prey)
				printf("[ -- ] %-22s %s\n", "melee with a weapon", "skipped: no small NPC (a_..., level 3 or less) in the zone");
			else if (!summoned)
				Step(false, "melee with a weapon", "the weapon was not summoned");
			else
			{
				MoveItem(z, kCursor, 13);	// primary hand (what was there comes to the cursor)
				// Where the NPC is now (it may walk): the latest of its position updates (MobUpdate:
				// int32 count, then 15-byte SpawnPositionUpdate_Struct: id, y at 5, x at 7, z x10 at 9).
				float px = prey->x, py = prey->y, pz = prey->z;
				got = Collect(z, 3000, [](const std::vector<Packet*>&) { return false; });
				for (size_t i = 0; i < got.size(); i++)
				{
					const Packet* p = got[i];
					if (p->opcode != kMobUpdate || p->size < 4)
						continue;
					int n = p->pBuffer[0] | (p->pBuffer[1] << 8);
					for (int k = 0; k < n && 4 + (k + 1) * 15 <= (int)p->size; k++)
					{
						const unsigned char* m = p->pBuffer + 4 + k * 15;
						if ((m[0] | (m[1] << 8)) != prey->id)
							continue;
						py = (short)(m[5] | (m[6] << 8));
						px = (short)(m[7] | (m[8] << 8));
						pz = (short)(m[9] | (m[10] << 8)) / 10.0f;
					}
				}
				DeleteAll(got);
				// Where the zone has it (#loc answers for the target): the spawn list and the updates
				// can be old, and a fish or a mosquito is not where its spawn point is.
				{
					uint16_t aim[2] = { (uint16_t)prey->id, 0 };
					z.Send(kClientTarget, aim, sizeof(aim));
					z.Poll(300);
					Say(z, charname, "#loc");
					std::string where;
					std::vector<Packet*> l = Collect(z, 5000, [&](const std::vector<Packet*>& g) { return HasText(g, "s Location:", &where); });
					float fx, fy, fz;
					size_t at = where.find("Location:");
					if (at != std::string::npos && sscanf(where.c_str() + at + 9, " %f, %f, %f", &fx, &fy, &fz) == 3)
					{
						px = fx;
						py = fy;
						pz = fz;
					}
					DeleteAll(l);
				}
				unsigned char u[15];
				memset(u, 0, sizeof(u));
				short y = (short)py, x = (short)(px + 2), zz = (short)((pz + 4) * 10);	// eye height rather than the feet: line of sight; a client sends z x10
				memcpy(u + 5, &y, 2); memcpy(u + 7, &x, 2); memcpy(u + 9, &zz, 2);
				u[3] = HeadingTowards(x, y, px, py);	// the zone only lets a player hit what it faces
				z.Send(kClientUpdate, u, sizeof(u));
				uint16_t target[2] = { (uint16_t)prey->id, 0 };
				z.Send(kClientTarget, target, sizeof(target));
				z.Poll(500);
				if (getenv("EQBOT_VERBOSE"))
				{
					Say(z, charname, "#loc");	// with a target: the target's place, as the zone has it
					std::vector<Packet*> l = Collect(z, 3000, [](const std::vector<Packet*>& g) { return HasText(g, "Location"); });
					printf("       placed at %d, %d, %d (x, y, z as sent); zone says %s\n", x, y, zz, LastTexts(l, 1).c_str());
					DeleteAll(l);
					uint16_t none[2] = { 0, 0 };
					z.Send(kClientTarget, none, sizeof(none));
					Say(z, charname, "#loc");
					l = Collect(z, 3000, [](const std::vector<Packet*>& g) { return HasText(g, "Location"); });
					printf("       and the zone has us at %s\n", LastTexts(l, 1).c_str());
					DeleteAll(l);
					z.Send(kClientTarget, target, sizeof(target));
					z.Poll(300);
				}
				uint32_t on = 1, off = 0;
				z.Send(kAutoAttack, &on, sizeof(on));
				// OP_Action: target (int32), source (int32), type (the attack skill: 0x01 slash, 0x04 punch...), damage at 12.
				// The NPC may walk or fly: the bot keeps standing next to it (its position updates).
				std::map<int, int> types;
				got.clear();
				long until = NowMs() + 15000;
				int hits = 0;
				while (NowMs() < until && hits < 4)
				{
					std::vector<Packet*> fresh = z.Poll(300);
					bool moved = false;
					for (size_t i = 0; i < fresh.size(); i++)
					{
						const Packet* p = fresh[i];
						if (p->opcode == kAction && p->size >= 16 && (p->pBuffer[0] | (p->pBuffer[1] << 8)) == prey->id)
							hits++;
						if (p->opcode == kDeath && p->size >= 18 && (p->pBuffer[0] | (p->pBuffer[1] << 8)) == prey->id)
							hits = 99;	// the last blow: Death_Struct carries its attack skill too
						if (p->opcode != kMobUpdate || p->size < 4)
							continue;
						int n = p->pBuffer[0] | (p->pBuffer[1] << 8);
						for (int k = 0; k < n && 4 + (k + 1) * 15 <= (int)p->size; k++)
						{
							const unsigned char* m = p->pBuffer + 4 + k * 15;
							if ((m[0] | (m[1] << 8)) != prey->id)
								continue;
							py = (short)(m[5] | (m[6] << 8));
							px = (short)(m[7] | (m[8] << 8));
							pz = (short)(m[9] | (m[10] << 8)) / 10.0f;
							moved = true;
						}
					}
					if (moved)
					{
						short ny = (short)py, nx = (short)(px + 2), nz = (short)((pz + 4) * 10);
						memcpy(u + 5, &ny, 2); memcpy(u + 7, &nx, 2); memcpy(u + 9, &nz, 2);
						u[3] = HeadingTowards(nx, ny, px, py);
						z.Send(kClientUpdate, u, sizeof(u));
					}
					if (getenv("EQBOT_VERBOSE"))
						for (size_t i = 0; i < fresh.size(); i++)
							if (fresh[i]->opcode != kMobUpdate)
								printf("       fight <- 0x%04x %d bytes  %s\n", (unsigned)(unsigned short)fresh[i]->opcode, (int)fresh[i]->size, HexDump(fresh[i]->pBuffer, fresh[i]->size, 20).c_str());
					got.insert(got.end(), fresh.begin(), fresh.end());
				}
				for (size_t i = 0; i < got.size(); i++)
				{
					if (got[i]->opcode == kAction && got[i]->size >= 16 && (got[i]->pBuffer[0] | (got[i]->pBuffer[1] << 8)) == prey->id)
						types[got[i]->pBuffer[8]]++;
					if (got[i]->opcode == kDeath && got[i]->size >= 18 && (got[i]->pBuffer[0] | (got[i]->pBuffer[1] << 8)) == prey->id)
						types[got[i]->pBuffer[14]]++;
				}
				std::string said = LastTexts(got, 3);
				DeleteAll(got);
				z.Send(kAutoAttack, &off, sizeof(off));
				std::string seen;
				for (std::map<int, int>::iterator it = types.begin(); it != types.end(); ++it)
				{
					char t[40];
					snprintf(t, sizeof(t), "%s0x%02x x%d", seen.empty() ? "" : ", ", it->first, it->second);
					seen += t;
				}
				bool slashes = !types.empty() && types.count(0x04) == 0 && types.count(0x01) > 0;
				Step(slashes, "melee with a weapon", (types.empty() ? std::string("no hit on ") + prey->name + "; " + said : "attack types " + seen + " on " + prey->name + " (0x01 slash, 0x04 punch)"));
				// Tidy up: the NPC dies, the weapon is destroyed.
				Say(z, charname, "#kill");
				MoveItem(z, 13, kCursor);
				z.Poll(500);
				MoveItem(z, kCursor, kDestroy);
				DeleteAll(got = Collect(z, 1500, [](const std::vector<Packet*>&) { return false; }));
			}
		}

		// A merchant sells: open the closest one, buy EQBOT_SHOP (default 5, \"-\" skips) of its cheapest
		// stack. The money it costs is checked from outside (merchant-test.sh reads the database).
		const char* shopEnv = getenv("EQBOT_SHOP");
		if (shopEnv && strcmp(shopEnv, "-") == 0)
			printf("[ -- ] %-22s %s\n", "merchant buy", "skipped (EQBOT_SHOP=-)");
		else
		{
			int qty = shopEnv ? atoi(shopEnv) : 5;
			float meX = 0, meY = 0;
			if (pp.size() >= 2416) { memcpy(&meY, &pp[2408], 4); memcpy(&meX, &pp[2412], 4); }
			const SpawnInfo* merchant = 0;
			float best = 1e30f;
			for (size_t i = 0; i < spawns.size(); i++)
			{
				if (spawns[i].npc != 1 || (spawns[i].cls != 41 && spawns[i].cls != 32))	// Sony's 41; our MERCHANT is 32
					continue;
				// EQBOT_SHOP_NPC=<name prefix> picks one merchant whatever the distance
				if (getenv("EQBOT_SHOP_NPC") && spawns[i].name.compare(0, strlen(getenv("EQBOT_SHOP_NPC")), getenv("EQBOT_SHOP_NPC")) != 0)
					continue;
				float d = (spawns[i].x - meX) * (spawns[i].x - meX) + (spawns[i].y - meY) * (spawns[i].y - meY);
				if (d < best) { best = d; merchant = &spawns[i]; }
			}
			if (!merchant)
			{
				std::set<int> classes;
				for (size_t i = 0; i < spawns.size(); i++) if (spawns[i].npc == 1) classes.insert(spawns[i].cls);
				std::string cl;
				for (std::set<int>::iterator c = classes.begin(); c != classes.end(); ++c) cl += std::to_string(*c) + " ";
				printf("[ -- ] %-22s %s\n", "merchant buy", ("skipped: no merchant in the zone (NPC classes " + cl + ")").c_str());
			}
			else
			{
				uint16_t aim[2] = { (uint16_t)merchant->id, 0 };
				z.Send(kClientTarget, aim, sizeof(aim));
				z.Poll(300);
				Say(z, charname, "#loc");
				std::string where;
				std::vector<Packet*> l = Collect(z, 5000, [&](const std::vector<Packet*>& g) { return HasText(g, "s Location:", &where); });
				DeleteAll(l);
				float mx = merchant->x, my = merchant->y, mz = merchant->z;
				size_t at = where.find("Location:");
				if (at != std::string::npos)
					sscanf(where.c_str() + at + 9, " %f, %f, %f", &mx, &my, &mz);
				unsigned char u[15];
				memset(u, 0, sizeof(u));
				short y = (short)my, x = (short)(mx + 3), zz = (short)((mz + 4) * 10);
				memcpy(u + 5, &y, 2); memcpy(u + 7, &x, 2); memcpy(u + 9, &zz, 2);
				u[3] = HeadingTowards(x, y, mx, my);
				z.Send(kClientUpdate, u, sizeof(u));
				z.Poll(500);
				// OP_ShopRequest: Merchant_Click_Struct (entity id, player id, 4 bytes, price multiplier)
				unsigned char click[16];
				memset(click, 0, sizeof(click));
				click[0] = merchant->id & 0xff; click[1] = merchant->id >> 8;
				z.Send(0x0b20, click, sizeof(click));
				std::vector<Packet*> shop = Collect(z, 4000, [](const std::vector<Packet*>&) { return false; });
				bool opened = false;
				int slot = -1, item = 0, cost = 0, cheapest = 0x7fffffff, items = 0;
				for (size_t i = 0; i < shop.size(); i++)
				{
					const Packet* p = shop[i];
					if (p->opcode == 0x0b20 && p->size >= 9 && p->pBuffer[8] == 1)
						opened = true;
					if (p->opcode != 0x0c20 || p->size < 5 + 193)
						continue;
					items++;
					const unsigned char* it = p->pBuffer + 5;
					int c = it[140] | (it[141] << 8) | (it[142] << 16) | (it[143] << 24);
					if (it[187] == 1 && c > 0 && c < cheapest)
					{
						cheapest = c;
						cost = c;
						item = it[130] | (it[131] << 8);
						slot = (short)(it[134] | (it[135] << 8));
					}
				}
				DeleteAll(shop);
				if (!opened || slot < 0)
					Step(false, "merchant buy", std::string(opened ? "no stack for sale" : "the shop did not open") + " (" + merchant->name + ", " + std::to_string(items) + " items)");
				else
				{
					// OP_ShopPlayerBuy: Merchant_Purchase_Struct (npc, player, slot, 2 bytes, quantity at 12, cost at 16)
					unsigned char buy[20];
					memset(buy, 0, sizeof(buy));
					buy[0] = merchant->id & 0xff; buy[1] = merchant->id >> 8;
					buy[8] = slot & 0xff; buy[9] = slot >> 8;
					buy[12] = (unsigned char)qty;
					z.Send(0x2720, buy, sizeof(buy));
					std::vector<Packet*> r = Collect(z, 4000, [](const std::vector<Packet*>& g) {
						for (size_t i = 0; i < g.size(); i++) if (g[i]->opcode == 0x2720) return true;
						return HasText(g, "afford"); });
					int gotQty = -1, gotCost = -1;
					for (size_t i = 0; i < r.size(); i++)
						if (r[i]->opcode == 0x2720 && r[i]->size >= 20)
						{
							gotQty = r[i]->pBuffer[12];
							gotCost = r[i]->pBuffer[16] | (r[i]->pBuffer[17] << 8) | (r[i]->pBuffer[18] << 16) | (r[i]->pBuffer[19] << 24);
						}
					std::string said = LastTexts(r, 1);
					DeleteAll(r);
					z.Send(0x3720, 0, 0);	// close the window
					DeleteAll(r = Collect(z, 800, [](const std::vector<Packet*>&) { return false; }));
					char b[200];
					if (gotQty >= 0)
						snprintf(b, sizeof(b), "item %d x%d from %s, unit cost %d, merchant says total %d", item, gotQty, merchant->name.c_str(), cost, gotCost);
					else
						snprintf(b, sizeof(b), "item %d x%d from %s refused: %s", item, qty, merchant->name.c_str(), said.c_str());
					Step(gotQty >= 0 || said.find("afford") != std::string::npos, "merchant buy", b);
				}
			}
		}

		// A quest NPC answers a hail (the zone's Perl quests work).
		const char* hailName = getenv("EQBOT_HAIL") ? getenv("EQBOT_HAIL") : "Brohan_Ironforge";
		if (strcmp(hailName, "-") == 0)
		{
			printf("[ -- ] %-22s %s\n", "quest hail", "skipped (EQBOT_HAIL=-)");
			g_spawns = 0;
			z.Disconnect();
			return failures ? 1 : 0;
		}
		SpawnInfo found;
		auto findNpc = [&]() {
			for (size_t i = 0; i < spawns.size(); i++)
				if (spawns[i].npc == 1 && spawns[i].name.compare(0, strlen(hailName), hailName) == 0)
				{
					found = spawns[i];
					return true;
				}
			return false; };
		if (!findNpc())
			DeleteAll(got = Collect(z, 15000, [&](const std::vector<Packet*>&) { return findNpc(); }));
		const SpawnInfo* npc = findNpc() ? &found : 0;
		if (!npc)
			Step(false, "quest hail", std::string(hailName) + " not in " + st.zone + " (EQBOT_HAIL=<npc name>)");
		else
		{
			// Stand next to the NPC (the zone takes the client's word for its position), target it, hail.
			unsigned char u[15];
			memset(u, 0, sizeof(u));
			u[0] = meId & 0xFF; u[1] = meId >> 8;
			short y = (short)npc->y, x = (short)(npc->x + 3), zz = (short)(npc->z * 10);
			memcpy(u + 5, &y, 2); memcpy(u + 7, &x, 2); memcpy(u + 9, &zz, 2);
			z.Send(kClientUpdate, u, sizeof(u));
			uint16_t target[2] = { (uint16_t)npc->id, 0 };
			z.Send(kClientTarget, target, sizeof(target));
			z.Poll(1000);
			std::string display = npc->name;
			for (size_t i = 0; i < display.size(); i++) if (display[i] == '_') display[i] = ' ';
			while (!display.empty() && isdigit((unsigned char)display[display.size() - 1])) display.erase(display.size() - 1);
			Say(z, charname, "Hail, " + display);
			got = Collect(z, 10000, [&](const std::vector<Packet*>& g) { return HasText(g, display + ":"); });
			bool answered = HasText(got, display + ":", &text);
			Step(answered, "quest hail", answered ? text.substr(0, 90) : LastTexts(got));
			DeleteAll(got);
		}

		g_spawns = 0;
		z.Disconnect();
		return failures ? 1 : 0;
	}

	// ---- walk: a bot that walks waypoints (bots milestone 1, docs/bots-design.md) ----

	struct Waypoint { float x, y, z; };

	// One decision line per action, the format docs/bots-design.md 6.1 verifies the milestones on.
	void BotLog(const std::string& bot, const std::string& zone, const char* fmt, ...)
	{
		char text[400];
		va_list args;
		va_start(args, fmt);
		vsnprintf(text, sizeof(text), fmt, args);
		va_end(args);
		struct timeval tv;
		gettimeofday(&tv, 0);
		printf("t=%ld.%03ld bot=%s zone=%s %s\n", (long)tv.tv_sec, (long)(tv.tv_usec / 1000), bot.c_str(), zone.c_str(), text);
		fflush(stdout);
	}

	int Walk(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname,
	         const std::string& file, int seconds)
	{
		std::vector<Waypoint> wps;
		FILE* f = fopen(file.c_str(), "r");
		if (f)
		{
			Waypoint w;
			while (fscanf(f, "%f %f %f", &w.x, &w.y, &w.z) == 3)
				wps.push_back(w);
			fclose(f);
		}
		if (wps.size() < 2)
		{
			fprintf(stderr, "walk: %s needs at least 2 waypoints (x y z per line)\n", file.c_str());
			return 2;
		}
		EqSession z;
		ZoneState st;
		if (!EnterZone(host, user, pass, charname, z, st))
		{
			BotLog(charname, "-", "action=Login result=failed");
			return 1;
		}
		BotLog(charname, st.zone, "action=EnterZone id=%d waypoints=%d", st.myId, (int)wps.size());
		const float speed = getenv("EQBOT_SPEED") ? atof(getenv("EQBOT_SPEED")) : 20.0f;	// units/s, about a walk
		const int tickMs = 250;
		std::vector<unsigned char> pp = DecodeProfile(st.profile);
		Waypoint at = wps[0];
		if (pp.size() >= 2420)
		{
			memcpy(&at.y, &pp[2408], 4);
			memcpy(&at.x, &pp[2412], 4);
			memcpy(&at.z, &pp[2416], 4);
		}
		long start = NowMs(), lastStatus = start, lastTick = start;
		long packetsIn = 0, packetsOut = 0, arrivals = 0;
		int next = 0, dir = 1;
		bool moving = false;
		while (NowMs() - start < seconds * 1000L)
		{
			const Waypoint& to = wps[next];
			float dx = to.x - at.x, dy = to.y - at.y, dz = to.z - at.z;
			float dist = sqrtf(dx * dx + dy * dy);
			if (!moving)
			{
				BotLog(charname, st.zone, "action=MoveTo wp=%d target=%.1f,%.1f,%.1f dist=%.1f", next, to.x, to.y, to.z, dist);
				moving = true;
			}
			long now = NowMs();
			float step = speed * (now - lastTick) / 1000.0f;	// the time really gone since the last update
			lastTick = now;
			bool arrived = dist <= step;
			if (arrived)
				at = to;
			else
			{
				at.x += dx / dist * step;
				at.y += dy / dist * step;
				at.z += dz / dist * step;	// between two grid points the ground is close to the line
			}
			unsigned char u[15];
			memset(u, 0, sizeof(u));
			short y = (short)at.y, x = (short)at.x, zz = (short)(at.z * 10);	// a client sends z x10
			u[0] = st.myId & 0xff; u[1] = st.myId >> 8;
			u[2] = arrived ? 0 : (unsigned char)(speed > 30 ? 46 : 22);	// animation: walking pace
			u[3] = HeadingTowards(at.x, at.y, to.x, to.y);
			memcpy(u + 5, &y, 2); memcpy(u + 7, &x, 2); memcpy(u + 9, &zz, 2);
			z.Send(kClientUpdate, u, sizeof(u));
			packetsOut++;
			// wait out the tick (Poll returns as soon as a packet comes), keep our id when it comes
			long tickEnd = NowMs() + tickMs;
			while (NowMs() < tickEnd)
			{
				std::vector<Packet*> got = z.Poll((int)(tickEnd - NowMs()));
				packetsIn += got.size();
				for (size_t i = 0; i < got.size(); i++)
					if (got[i]->opcode == (int16)0xf520 && got[i]->size >= 12 && (got[i]->pBuffer[4] | (got[i]->pBuffer[5] << 8)) == 16
						&& st.myId == 0)
					{
						st.myId = got[i]->pBuffer[8] | (got[i]->pBuffer[9] << 8);
						BotLog(charname, st.zone, "action=Identify id=%d", st.myId);
					}
				DeleteAll(got);
			}
			if (arrived)
			{
				arrivals++;
				moving = false;
				BotLog(charname, st.zone, "action=Arrive wp=%d at=%.1f,%.1f,%.1f", next, at.x, at.y, at.z);
				// EQBOT_VERIFY (GM accounts only): every 5th arrival, where does the zone have us?
				if (getenv("EQBOT_VERIFY") && arrivals % 5 == 0)
				{
					uint16_t none[2] = { 0, 0 };
					z.Send(kClientTarget, none, sizeof(none));
					Say(z, charname, "#loc");
					std::string where;
					std::vector<Packet*> l = Collect(z, 5000, [&](const std::vector<Packet*>& g) { return HasText(g, "Current Location:", &where); });
					packetsIn += l.size();
					DeleteAll(l);
					float sx, sy, sz;
					size_t pos = where.find("Location:");
					if (pos != std::string::npos && sscanf(where.c_str() + pos + 9, " %f, %f, %f", &sx, &sy, &sz) == 3)
						BotLog(charname, st.zone, "action=Verify server=%.1f,%.1f,%.1f diff=%.1f", sx, sy, sz,
							sqrtf((sx - at.x) * (sx - at.x) + (sy - at.y) * (sy - at.y)));
					else
						BotLog(charname, st.zone, "action=Verify result=no-answer");
					lastTick = NowMs();
				}
				// rest a moment at every third point: sit, then stand
				if (arrivals % 3 == 0)
				{
					unsigned char sa[12];
					memset(sa, 0, sizeof(sa));
					sa[0] = st.myId & 0xff; sa[1] = st.myId >> 8; sa[4] = 14; sa[8] = 110;
					z.Send((int16)0xf520, sa, sizeof(sa));
					BotLog(charname, st.zone, "action=Sit");
					long until = NowMs() + 3000;
					while (NowMs() < until) { std::vector<Packet*> g = z.Poll(250); packetsIn += g.size(); DeleteAll(g); }
					lastTick = NowMs();	// sitting is not walking
					sa[8] = 100;
					z.Send((int16)0xf520, sa, sizeof(sa));
					packetsOut += 2;
					BotLog(charname, st.zone, "action=Stand");
				}
				if (next + dir < 0 || next + dir >= (int)wps.size())
					dir = -dir;		// walk the grid back and forth
				next += dir;
			}
			if (NowMs() - lastStatus >= 30000)
			{
				double secs = (NowMs() - start) / 1000.0;
				BotLog(charname, st.zone, "action=Status pos=%.1f,%.1f,%.1f arrivals=%ld in_pps=%.1f out_pps=%.1f", at.x, at.y, at.z, arrivals, packetsIn / secs, packetsOut / secs);
				lastStatus = NowMs();
			}
		}
		double secs = (NowMs() - start) / 1000.0;
		BotLog(charname, st.zone, "action=Done arrivals=%ld seconds=%.0f in_pps=%.1f out_pps=%.1f", arrivals, secs, packetsIn / secs, packetsOut / secs);
		z.Disconnect();
		return 0;
	}


	// ---- sensors: what a player would find wrong, one line each (tools/botd/bugreport.py groups them) ----
	// action=Anomaly kind=<kind> ...; the same kind from the same bot at most once a minute, with the
	// number of repeats it stood for.
	void Anomaly(const std::string& bot, const std::string& zone, const char* kind, const char* fmt, ...)
	{
		static std::map<std::string, std::pair<long, int> > last;	// kind -> (time, repeats since)
		std::pair<long, int>& l = last[kind];
		long now = NowMs();
		if (l.first && now - l.first < 60000) { l.second++; return; }
		char detail[400];
		va_list ap;
		va_start(ap, fmt);
		vsnprintf(detail, sizeof(detail), fmt, ap);
		va_end(ap);
		BotLog(bot, zone, "action=Anomaly kind=%s %s repeats=%d", kind, detail, l.second);
		l = std::make_pair(now, 0);
	}

	// A spawn the client could not make sense of (a corpse 1e22 big took every click)
	void CheckSpawn(const std::string& bot, const std::string& zone, const SpawnInfo& si)
	{
		if (!(si.size >= -1 && si.size <= 100))
			Anomaly(bot, zone, "bad_spawn", "spawn=%s(%d) why=size size=%g", si.name.c_str(), si.id, si.size);
		else if (si.size < 0)	// 1339 npc_types rows hold -1 (the import's "race default"?): data to look at
			Anomaly(bot, zone, "npc_data", "spawn=%s(%d) why=negative_size size=%g", si.name.c_str(), si.id, si.size);
		else if (si.npc == 1 && (si.level == 0 || si.level > 100))
			Anomaly(bot, zone, "bad_spawn", "spawn=%s(%d) why=level level=%d", si.name.c_str(), si.id, si.level);
		else if (fabsf(si.x) > 30000 || fabsf(si.y) > 30000 || si.name.empty())
			Anomaly(bot, zone, "bad_spawn", "spawn=%s(%d) why=position pos=%.0f,%.0f", si.name.c_str(), si.id, si.x, si.y);
	}

	// ---- chat (milestone 4): the bots ask tools/botd/chatd.py for their words ----
	// One request at a time per bot, on its own thread: the game loop never waits for it, and an
	// answer older than 10 s is dropped. CHATD_PORT (default 7780) on 127.0.0.1.

	std::string JsonEscape(const std::string& in)
	{
		std::string out;
		for (size_t i = 0; i < in.size(); i++)
		{
			unsigned char c = in[i];
			if (c == '"' || c == '\\') { out += '\\'; out += c; }
			else if (c < 0x20) { char b[8]; snprintf(b, sizeof(b), "\\u%04x", c); out += b; }
			else if (c < 0x80) out += c;
			else { out += (char)(0xc0 | (c >> 6)); out += (char)(0x80 | (c & 0x3f)); }	// game text is Latin-1
		}
		return out;
	}

	// The string value of "key" in a flat JSON object (enough for chatd's answers).
	std::string JsonString(const std::string& json, const std::string& key)
	{
		size_t k = json.find("\"" + key + "\"");
		if (k == std::string::npos) return "";
		size_t q = json.find('"', json.find(':', k) + 1);
		if (q == std::string::npos) return "";
		std::string out;
		for (size_t i = q + 1; i < json.size() && json[i] != '"'; i++)
		{
			if (json[i] == '\\' && i + 1 < json.size())
			{
				i++;
				if (json[i] == 'u' && i + 4 < json.size())
				{
					int cp = (int)strtol(json.substr(i + 1, 4).c_str(), 0, 16);
					out += cp < 256 ? (char)cp : '?';	// back to Latin-1 for the game
					i += 4;
				}
				else out += json[i] == 'n' ? ' ' : json[i];
			}
			else if ((unsigned char)json[i] >= 0xc0 && i + 1 < json.size())
			{
				int cp = ((json[i] & 0x1f) << 6) | (json[i + 1] & 0x3f);
				out += cp < 256 ? (char)cp : '?';
				i++;
			}
			else out += json[i];
		}
		return out;
	}

	std::string HttpPost(const std::string& path, const std::string& body, int timeoutSec)
	{
		int port = getenv("CHATD_PORT") ? atoi(getenv("CHATD_PORT")) : 7780;
		int fd = socket(AF_INET, SOCK_STREAM, 0);
		if (fd < 0) return "";
		struct timeval tv = { timeoutSec, 0 };
		setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
		setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &tv, sizeof(tv));
		sockaddr_in a;
		memset(&a, 0, sizeof(a));
		a.sin_family = AF_INET;
		a.sin_port = htons(port);
		a.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
		std::string out;
		if (connect(fd, (sockaddr*)&a, sizeof(a)) == 0)
		{
			char head[256];
			snprintf(head, sizeof(head), "POST %s HTTP/1.0\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\nContent-Length: %d\r\n\r\n",
			         path.c_str(), (int)body.size());
			std::string req = head + body;
			if (send(fd, req.data(), req.size(), 0) == (ssize_t)req.size())
			{
				char buf[4096];
				ssize_t n;
				while ((n = recv(fd, buf, sizeof(buf), 0)) > 0)
					out.append(buf, n);
			}
		}
		close(fd);
		size_t body0 = out.find("\r\n\r\n");
		return body0 == std::string::npos ? "" : out.substr(body0 + 4);
	}

	struct ChatJob
	{
		std::mutex m;
		bool done;
		std::string answer;
		ChatJob() : done(false) {}
	};

	struct ChatLine { std::string channel, to, text, source; long asked; };

	class ChatClient
	{
	public:
		ChatClient() : asked(0) {}
		// starts a request unless one is pending; `channel`/`to` say where the answer goes
		bool Ask(const std::string& path, const std::string& body, const std::string& channel, const std::string& to)
		{
			if (job) return false;
			job.reset(new ChatJob());
			std::shared_ptr<ChatJob> j = job;
			std::thread([j, path, body]() {
				std::string a = HttpPost(path, body, 10);
				std::lock_guard<std::mutex> l(j->m);
				j->answer = a;
				j->done = true;
			}).detach();
			replyChannel = channel;
			replyTo = to;
			asked = NowMs();
			return true;
		}
		// the answer when it came (empty text: nothing to say), false while pending
		bool Take(ChatLine& line)
		{
			if (!job) return false;
			std::string a;
			{
				std::lock_guard<std::mutex> l(job->m);
				if (!job->done)
				{
					if (NowMs() - asked < 10000) return false;
					a = "";	// too late: dropped (the thread ends on its own)
				}
				else a = job->answer;
			}
			job.reset();
			line.channel = replyChannel;
			line.to = replyTo;
			line.text = JsonString(a, "text");
			line.source = JsonString(a, "source");
			line.asked = asked;
			return true;
		}
	private:
		std::shared_ptr<ChatJob> job;
		std::string replyChannel, replyTo;
		long asked;
	};

	// OP_ChannelMessage: target[32], sender[32], language at 64, channel at 66, text at 70
	void SendChat(EqSession& z, const std::string& me, int channel, const std::string& to, const std::string& text)
	{
		std::vector<unsigned char> b(70 + text.size() + 1, 0);
		strncpy((char*)&b[0], to.c_str(), 31);
		strncpy((char*)&b[32], me.c_str(), 31);
		b[66] = channel;
		memcpy(&b[70], text.c_str(), text.size());
		z.Send(kChannelMessage, &b[0], b.size());
	}

	const char* ClassName(int c)
	{
		static const char* n[] = { "?", "warrior", "cleric", "paladin", "ranger", "shadowknight", "druid", "monk", "bard",
		                           "rogue", "shaman", "necromancer", "wizard", "magician", "enchanter" };
		return c >= 0 && c < 15 ? n[c] : "?";
	}

	const char* RaceName(int r)
	{
		static const char* n[] = { "?", "human", "barbarian", "erudite", "wood elf", "high elf", "dark elf", "half elf", "dwarf",
		                           "troll", "ogre", "halfling", "gnome" };
		return r >= 0 && r < 13 ? n[r] : r == 128 ? "iksar" : "?";
	}

	// ---- hunt: a bot that hunts alone (bots milestone 2, docs/bots-design.md 9) ----
	// A state machine, one decision line per change (as walk):
	//   Seek     closest prey near the camp: "a_"/"an_" NPC, small enough, nobody on it, not a guard
	//   Consider ask the zone (OP_Consider): never attack an NPC that cons amiable or better
	//   Approach walk to it; Fight: target + auto-attack; Loot: every item of its corpse
	//   Rest     sit until healed (under 90 % HP before a pull, and a short sit after each fight); stand on aggro
	//   Flee     back to the camp under 20 % HP while the prey is still healthy
//   Avoid    (Seek/Rest) a named 3+ levels above us within 70: walk away; prey near one is left alone
//   It eats and drinks from its packs when the zone says it is getting hungry (a famished player heals nothing).
//   Out of food or drink (or every EQBOT_TOWN_EVERY minutes), with EQBOT_TOWN set: a trip to town between two
//   lives (TownTrip, milestone 5): sell the packs, buy drink and food, come back.
	// A bot that is hit fights back whatever it was doing. Killed: it logs, waits, and logs in again
	// (the zone sends it to its bind point). Spells: Minor Healing on self when a gem holds it.

	struct Mobile { std::string name; int level; float x, y, z; int hp; bool alive; bool touched = false; };	// touched: a player other than us hit it

	struct HuntStats
	{
		int kills, loots, items, deaths, hitsDealt, damageDealt, hitsTaken, damageTaken, refused, recovered, equipped, assists, casts;
		long expGained;
		bool campSet;	// the camp is where the first life began: the bot goes back there after a death
		float campX, campY, campZ;
		std::set<std::string> emptiedCorpses;	// our own corpses already looted
		std::vector<std::pair<float, float> > zoneLines;	// where the zone tried to send us elsewhere
		bool sayDeath;	// died: say so to the group once back
		std::map<int, int> corpseFood, corpseDrink;	// the food and drink left on our last corpse that had some
		bool wantTown;	// out of food or drink: go to town (EQBOT_TOWN), sell, buy, come back
		long lastTown;
		int townTrips;
		HuntStats() : kills(0), loots(0), items(0), deaths(0), hitsDealt(0), damageDealt(0), hitsTaken(0), damageTaken(0),
		              refused(0), recovered(0), equipped(0), assists(0), casts(0), expGained(0), campSet(false), campX(0), campY(0), campZ(0), sayDeath(false),
		              wantTown(false), lastTown(0), townTrips(0) {}
	};

	// Items of the bulk inventory sent at zone-in: slot -> Item_Struct bytes.
	std::map<int, std::vector<unsigned char> > InventoryItems(const std::vector<unsigned char>& packet)
	{
		std::map<int, std::vector<unsigned char> > items;
		if (packet.size() < 3)
			return items;
		int count = packet[0] | (packet[1] << 8);
		std::vector<unsigned char> raw = Inflate(std::vector<unsigned char>(packet.begin() + 2, packet.end()), 200000);
		if (count <= 0 || raw.size() % count)
			return items;
		size_t rec = raw.size() / count;	// int16 opcode + Item_Struct
		for (int i = 0; i < count && rec >= 140; i++)
		{
			const unsigned char* it = &raw[i * rec + 2];
			int slot = (short)(it[134] | (it[135] << 8));
			items[slot] = std::vector<unsigned char>(it, it + rec - 2);
		}
		return items;
	}

	// Puts on what lies in the packs and fits an empty worn slot (after looting our corpse), as a player
	// does: pack -> cursor (slot 0) -> worn slot. Weapons first in the primary hand.
	int EquipFromPacks(EqSession& z, const std::string& charname, const std::string& zone, const std::vector<unsigned char>& inventory)
	{
		std::map<int, std::vector<unsigned char> > items = InventoryItems(inventory);
		if (getenv("EQBOT_VERBOSE"))
			for (std::map<int, std::vector<unsigned char> >::iterator i = items.begin(); i != items.end(); ++i)
				printf("       item slot=%d nr=%d size=%d slots=0x%08x\n", i->first, i->second[130] | (i->second[131] << 8), (int)i->second.size(),
				       i->second[136] | (i->second[137] << 8) | (i->second[138] << 16) | ((unsigned)i->second[139] << 24));
		static const int order[] = { 13, 14, 11, 2, 17, 18, 19, 7, 12, 6, 8, 20, 5, 1, 4, 3, 9, 10, 15, 16, 21 };
		int moved = 0;
		for (int from = 22; from <= 29; from++)
		{
			if (!items.count(from))
				continue;
			const std::vector<unsigned char> it = items[from];
			if (it.size() < 140)
				continue;
			uint32_t slots = it[136] | (it[137] << 8) | (it[138] << 16) | ((uint32_t)it[139] << 24);
			for (size_t k = 0; k < sizeof(order) / sizeof(order[0]); k++)
			{
				int to = order[k];
				if (!(slots & (1u << to)) || items.count(to))
					continue;
				uint32_t a[3] = { (uint32_t)from, 0, 0 }, b[3] = { 0, (uint32_t)to, 0 };
				z.Send((int16)0x2c21, a, sizeof(a));	// OP_MoveItem: pick up
				z.Send((int16)0x2c21, b, sizeof(b));	// put on
				std::vector<Packet*> got = z.Poll(300);
				DeleteAll(got);
				items[to] = it;
				BotLog(charname, zone, "action=Equip item=%d from=%d to=%d", it[130] | (it[131] << 8), from, to);
				moved++;
				break;
			}
		}
		return moved;
	}

	bool IsPreyName(const std::string& n)
	{
		if (n.compare(0, 2, "a_") != 0 && n.compare(0, 3, "an_") != 0)
			return false;
		std::string l = n;
		for (size_t i = 0; i < l.size(); i++)
			l[i] = tolower(l[i]);
		return l.find("guard") == std::string::npos && l.find("merchant") == std::string::npos;
	}

	// One life: from zone-in to death or the deadline. Returns false when the login failed.
	bool HuntLife(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname,
	              long deadline, HuntStats& hs, bool& died, bool& relog)
	{
		died = false;
		relog = false;
		EqSession z;
		ZoneState st;
		if (!EnterZone(host, user, pass, charname, z, st))
		{
			BotLog(charname, "-", "action=Login result=failed");
			return false;
		}
		std::vector<unsigned char> pp = DecodeProfile(st.profile);
		float meX = 0, meY = 0, meZ = 0;
		if (pp.size() >= 2420) { memcpy(&meY, &pp[2408], 4); memcpy(&meX, &pp[2412], 4); memcpy(&meZ, &pp[2416], 4); }
		if (!hs.campSet) { hs.campX = meX; hs.campY = meY; hs.campZ = meZ; hs.campSet = true; }
		const float campX = hs.campX, campY = hs.campY, campZ = hs.campZ, campRadius = 800;
		float roamX = campX, roamY = campY;
		int myLevel = pp.size() > 60 ? pp[60] : 1;
		int myHp = 100, myMaxHp = 0;	// percent; unknown until the zone sends our HP
		long myExp = -1;
		// spells in the gems (setup-accounts.sh scribes Minor Healing for clerics, Frost Bolt for wizards)
		const int kMinorHealing = 200, kMinorHealingMana = 10, kFrostBolt = 54, kFrostBoltMana = 10;
		int healGem = -1, nukeGem = -1;
		for (int g = 0; g < 8 && pp.size() >= 2406; g++)
		{
			int sp = pp[2390 + g * 2] | (pp[2391 + g * 2] << 8);
			if (sp == kMinorHealing) healGem = g;
			if (sp == kFrostBolt) nukeGem = g;
		}
		int myMana = pp.size() >= 72 ? (short)(pp[70] | (pp[71] << 8)) : 0, maxMana = myMana;
		long castUntil = 0, lastManaRise = 0;
		if (pp.size() >= 68) { uint32_t e; memcpy(&e, &pp[64], 4); myExp = e; }
		std::map<int, Mobile> mobs;
		std::vector<SpawnInfo> spawns = DecodeSpawns(st.spawnPackets);
		for (size_t i = 0; i < st.newSpawnPackets.size(); i++)
		{
			std::vector<unsigned char> d = st.newSpawnPackets[i];
			Decrypt(d, 0, 0x65e7, 9, 13, false);
			spawns.push_back(ParseSpawn(&d[4]));
		}
		// our own corpses still in the zone: "<name>'s corpse<n>" (or with an underscore)
		std::map<int, Mobile> myCorpses;
		std::map<int, Mobile> players;	// other players (group members among them), by spawn id
		auto addPlayer = [&](const SpawnInfo& si) {
			if (si.npc != 0 || si.name == charname || si.name.find("corpse") != std::string::npos)
				return;
			for (std::map<int, Mobile>::iterator it = players.begin(); it != players.end(); ++it)
				if (it->second.name == si.name) { players.erase(it); break; }	// logged in again: a new id
			players[si.id] = Mobile{ si.name, si.level, si.x, si.y, si.z, 100, true };
		};
		if (pp.empty())
			Anomaly(charname, st.zone, "no_profile", "why=zone_sent_no_player_profile");
		for (size_t i = 0; i < spawns.size(); i++)
		{
			const SpawnInfo& si = spawns[i];
			CheckSpawn(charname, st.zone, si);
			if (si.npc == 1)
				mobs[si.id] = Mobile{ si.name, si.level, si.x, si.y, si.z, 100, true };
			else if (si.name.compare(0, charname.size() + 2, charname + "'s") == 0 && !hs.emptiedCorpses.count(si.name))
			{
				myCorpses[si.id] = Mobile{ si.name, si.level, si.x, si.y, si.z, 100, true };
				BotLog(charname, "-", "action=SeeCorpse name=%s size=%g npc=%d", si.name.c_str(), si.size, si.npc);
			}
			else
				addPlayer(si);
		}
		// group (milestone 3): a leader invites EQBOT_INVITE; a member (EQBOT_ROLE=member) never pulls
		std::vector<std::string> invitees;
		if (getenv("EQBOT_INVITE"))
		{
			std::string l = getenv("EQBOT_INVITE");
			for (size_t a = 0, b; a < l.size(); a = b + 1)
			{
				b = l.find(',', a);
				if (b == std::string::npos) b = l.size();
				if (b > a) invitees.push_back(l.substr(a, b - a));
			}
		}
		const bool member = getenv("EQBOT_ROLE") && std::string(getenv("EQBOT_ROLE")) == "member";
		bool grouped = false, following = true;
		std::string leaderName;
		std::set<std::string> members, medding;	// group members but us; who said "oom"
		std::map<std::string, long> lastInvite;
		auto playerByName = [&](const std::string& n) -> int {
			for (std::map<int, Mobile>::iterator it = players.begin(); it != players.end(); ++it)
				if (it->second.name == n) return it->first;
			return 0;
		};
		BotLog(charname, st.zone, "action=EnterZone id=%d level=%d npcs=%d corpses=%d items=%d camp=%.0f,%.0f,%.0f", st.myId, myLevel, (int)mobs.size(),
		       (int)myCorpses.size(), (int)InventoryItems(st.inventory).size(), campX, campY, campZ);
		int equipped = EquipFromPacks(z, charname, st.zone, st.inventory);
		// food and drink in the inventory (item type 14 / 15): slot -> what is left of the stack. The real
		// client eats and drinks by itself when it gets hungry; famished, a player regenerates nothing
		std::map<int, int> food, drink;
		{
			std::map<int, std::vector<unsigned char> > items = InventoryItems(st.inventory);
			for (std::map<int, std::vector<unsigned char> >::iterator i = items.begin(); i != items.end(); ++i)
			{
				if (i->second.size() < 220 || i->first <= 0)
					continue;
				int type = i->second[194], left = (signed char)i->second[218];
				if (type == 14 && left > 0) food[i->first] = left;
				if (type == 15 && left > 0) drink[i->first] = left;
			}
		}
		long lastConsume = 0;
		bool saidHungry = false;
		auto consume = [&](std::map<int, int>& stock, int type, const char* what, int level) {
			if (stock.empty())
			{
				if (!saidHungry)
					BotLog(charname, st.zone, "action=Hungry why=no_%s level=%d", what, level);
				saidHungry = true;
				// a town to shop in (EQBOT_TOWN), nothing waiting on a corpse, and no trip in the last 20 minutes
				if (getenv("EQBOT_TOWN") && hs.corpseFood.empty() && hs.corpseDrink.empty() && (!hs.lastTown || NowMs() - hs.lastTown > 1200000))
					hs.wantTown = true;
				return;
			}
			unsigned char b[16];	// Consume_Struct: slot, 0xffffffff (the client did it by itself), 4 bytes, type
			memset(b, 0, sizeof(b));
			int slot = stock.begin()->first;
			b[0] = slot & 0xff; b[1] = (slot >> 8) & 0xff;
			b[4] = b[5] = b[6] = b[7] = 0xff;
			b[12] = (unsigned char)type;
			z.Send((int16)0x5621, b, sizeof(b));	// OP_ConsumeFoodDrink
			int left = --stock.begin()->second;
			BotLog(charname, st.zone, "action=Consume what=%s slot=%d left=%d level=%d", what, slot, left, level);
			if (left <= 0)
				stock.erase(stock.begin());
			lastConsume = NowMs();
		};
		hs.equipped += equipped;

		enum State { Seek, Consider, Approach, Fight, Loot, Rest, Flee, Dead, Recover } state = Seek;
		const char* names[] = { "Seek", "Consider", "Approach", "Fight", "Loot", "Rest", "Flee", "Dead", "Recover" };
		float fleeX = 0, fleeY = 0;
		long recoverSentAt = 0;
		std::vector<std::pair<float, float> >& zoneLines = hs.zoneLines;
		auto nearZoneLine = [&](float x, float y) {
			for (size_t k = 0; k < zoneLines.size(); k++)
				if (fabsf(zoneLines[k].first - x) < 80 && fabsf(zoneLines[k].second - y) < 80)
					return true;
			return false;
		};
		std::string recovering;	// the corpse we are going back to
		bool fledThisFight = false;
		long lastAssistCall = 0, lastMeleeMsg = 0, waitSince = 0;
		int castRefusals = 0;
		long expectExpBy = 0, lastPacket = NowMs(), silentSince = 0;	// a kill that should give experience; the zone's last word
		std::string expectExpFrom;
		int expectExpLevel = 0;
		bool sharedTarget = false;	// another player hit our target
		bool helpPending = false;	// leader: asked the zone what a member in trouble fights
		unsigned moveTick = 0;
		int tooFar = 0, cantSee = 0;
		bool closeIn = false, backOff = false;
		int target = 0;
		long start = NowMs(), lastTick = start, stateSince = start, lastStatus = start, lastHitTaken = 0, lastNoPrey = 0;
		const float speed = 20.0f;
		std::set<int> skip;				// prey we gave up on or must not attack
		std::map<int, int> faction;		// OP_Consider answers, by NPC id
		std::set<int> attackers;
		bool sitting = false;
		int lootedItems = 0, fightHits = 0;
		uint8_t lastHeading = 0, headingOffset = 0;
		long lastHitDealt = 0;
		auto setState = [&](State s, const char* why) {
			if (s == state) return;
			state = s;
			stateSince = NowMs();
			const char* tn = mobs.count(target) ? mobs[target].name.c_str() : myCorpses.count(target) ? myCorpses[target].name.c_str() : "-";
			BotLog(charname, st.zone, "action=%s target=%s(%d) hp=%d%% why=%s", names[s], tn, target, myHp, why);
		};
		auto sendPos = [&](float tx, float ty, bool walking) {
			unsigned char u[15];
			memset(u, 0, sizeof(u));
			short y = (short)meY, x = (short)meX, zz = (short)(meZ * 10);
			u[0] = st.myId & 0xff; u[1] = st.myId >> 8;
			u[2] = walking ? 22 : 0;
			// right on top of it, the direction means nothing: keep the last heading
			if ((tx - meX) * (tx - meX) + (ty - meY) * (ty - meY) > 0.25f)
				lastHeading = HeadingTowards(meX, meY, tx, ty);
			// standing: plus the turn we are trying, if the zone says we do not face the target
			u[3] = walking ? lastHeading : (uint8_t)(lastHeading + headingOffset);
			memcpy(u + 5, &y, 2); memcpy(u + 7, &x, 2); memcpy(u + 9, &zz, 2);
			// The zone passes a player's update on only when its deltas change (else every 10 s): a
			// walking bot flips the lowest delta_y bit, which the zone divides away (delta / 125) before
			// other clients see it, so that every step reaches the others.
			if (walking)
				u[11] = (++moveTick & 1) ? 1 : 2;
			z.Send(kClientUpdate, u, sizeof(u));
		};
		auto sit = [&](bool down) {
			if (down == sitting) return;
			unsigned char sa[12];
			memset(sa, 0, sizeof(sa));
			sa[0] = st.myId & 0xff; sa[1] = st.myId >> 8; sa[4] = 14; sa[8] = down ? 110 : 100;
			z.Send((int16)0xf520, sa, sizeof(sa));
			sitting = down;
			BotLog(charname, st.zone, "action=%s hp=%d%%", down ? "Sit" : "Stand", myHp);
		};
		auto cast = [&](int gem, int spell, int mana, int on, const char* spellName, const std::string& onName, const char* why) {
			if (gem < 0 || myMana < mana || NowMs() < castUntil)
				return false;
			unsigned char c[16];	// CastSpell_Struct: slot, spell, inventory slot (0xffff: a gem), target
			memset(c, 0, sizeof(c));
			c[0] = gem;
			c[2] = spell & 0xff; c[3] = spell >> 8;
			c[4] = 0xff; c[5] = 0xff;
			c[8] = on & 0xff; c[9] = on >> 8;
			sit(false);
			z.Send((int16)0x7e21, c, sizeof(c));	// OP_CastSpell
			castUntil = NowMs() + 3000;	// cast time, then the gem's recast; no moving meanwhile
			hs.casts++;
			BotLog(charname, st.zone, "action=Cast spell=%s target=%s hp=%d%% mana=%d why=%s", spellName, onName.c_str(), myHp, myMana, why);
			return true;
		};
		auto castHeal = [&](const char* why) {
			return cast(healGem, kMinorHealing, kMinorHealingMana, st.myId, "Minor_Healing", charname, why);
		};
		// /gsay: the client sends one ChannelMessage per member, its name first, channel 2
		auto gsay = [&](const std::string& text) {
			std::set<std::string> to = members;
			if (!leaderName.empty() && leaderName != charname) to.insert(leaderName);
			for (std::set<std::string>::iterator it = to.begin(); it != to.end(); ++it)
			{
				std::vector<unsigned char> m(70 + text.size() + 1, 0);
				strncpy((char*)&m[0], it->c_str(), 31);
				strncpy((char*)&m[32], charname.c_str(), 31);
				m[66] = 2;
				memcpy(&m[70], text.c_str(), text.size());
				z.Send(kChannelMessage, &m[0], m.size());
			}
			if (!to.empty())
				BotLog(charname, st.zone, "action=GSay text=%s", text.c_str());
		};
		// chat (milestone 4): who we are for chatd, what we heard, the one request in flight
		ChatClient chat;
		std::vector<std::string> heard;
		const int myRace = pp.size() > 57 ? pp[56] : 0, myClass = pp.size() > 58 ? pp[58] : 0;
		auto personaJson = [&]() {
			char b[160];
			snprintf(b, sizeof(b), "{\"race\":\"%s\",\"class\":\"%s\",\"level\":%d}", RaceName(myRace), ClassName(myClass), myLevel);
			return std::string(b);
		};
		auto askLine = [&](const char* kind, const std::string& channel, const std::string& extraVars) {
			char b[400];
			snprintf(b, sizeof(b), "{\"bot\":\"%s\",\"kind\":\"%s\",\"channel\":\"%s\",\"vars\":{\"level\":%d,\"class\":\"%s\",\"zone\":\"%s\"%s}}",
			         charname.c_str(), kind, channel.c_str(), myLevel, ClassName(myClass), st.zone.c_str(), extraVars.c_str());
			return chat.Ask("/line", b, channel, "");
		};
		auto addressed = [&](const std::string& channel, const std::string& from, const std::string& text) {
			std::string h = "[";
			for (size_t k = 0; k < heard.size(); k++)
				h += (k ? "," : "") + std::string("\"") + JsonEscape(heard[k]) + "\"";
			h += "]";
			std::string body = "{\"bot\":\"" + JsonEscape(charname) + "\",\"persona\":" + personaJson() + ",\"zone\":\"" + st.zone
			                   + "\",\"channel\":\"" + channel + "\",\"from\":\"" + JsonEscape(from) + "\",\"text\":\"" + JsonEscape(text)
			                   + "\",\"heard\":" + h + "}";
			if (chat.Ask("/reply", body, channel, from))
				BotLog(charname, st.zone, "action=Addressed channel=%s from=%s", channel.c_str(), from.c_str());
		};
		askLine("hello", "", "");	// chatd learns our name: bots never get LLM answers from bots
		long lastLfg = 0;
		auto attack = [&](bool on) {
			uint32_t v = on ? 1 : 0;
			z.Send(kAutoAttack, &v, sizeof(v));
		};
		// walks toward a point, stops `stop` units short; true when there
		long lastAvoid = 0;
		// NPCs we keep away from: 3 levels above us or more, neither a guard nor known to like us
		auto isDanger = [&](int id) {
			const Mobile& m = mobs[id];
			if (!m.alive || m.level < myLevel + 3)
				return false;
			std::string l = m.name;
			for (size_t i = 0; i < l.size(); i++)
				l[i] = tolower(l[i]);
			if (l.find("guard") != std::string::npos || l.find("merchant") != std::string::npos)
				return false;
			return !(faction.count(id) && faction[id] > 0);
		};
		auto nearDanger = [&](float x, float y) {
			for (std::map<int, Mobile>::iterator it = mobs.begin(); it != mobs.end(); ++it)
				if (fabsf(it->second.x - x) < 60 && fabsf(it->second.y - y) < 60 && isDanger(it->first))
					return true;
			return false;
		};
		auto walkTo = [&](float tx, float ty, float tz, float step, float stop) {
			float dx = tx - meX, dy = ty - meY, dist = sqrtf(dx * dx + dy * dy);
			if (dist <= stop + 1) { sendPos(tx, ty, false); return true; }
			if (NowMs() < castUntil) return false;	// moving breaks a cast
			float s = std::min(step, dist - stop);
			meX += dx / dist * s; meY += dy / dist * s; meZ = tz;
			sendPos(tx, ty, true);
			return false;
		};
		while (NowMs() < deadline && state != Dead)
		{
			long tickEnd = NowMs() + 250;
			while (NowMs() < tickEnd)
			{
				std::vector<Packet*> got = z.Poll((int)(tickEnd - NowMs()));
				if (!got.empty()) lastPacket = NowMs();
				for (size_t i = 0; i < got.size(); i++)
				{
					const Packet* p = got[i];
					const unsigned char* b = p->pBuffer;
					if (p->opcode == kMobUpdate && p->size >= 4)
					{
						int n = b[0] | (b[1] << 8);
						for (int k = 0; k < n && 4 + (k + 1) * 15 <= (int)p->size; k++)
						{
							const unsigned char* m = b + 4 + k * 15;
							int id = m[0] | (m[1] << 8);
							Mobile* o = mobs.count(id) ? &mobs[id] : players.count(id) ? &players[id] : 0;
							if (!o) continue;
							o->y = (short)(m[5] | (m[6] << 8));
							o->x = (short)(m[7] | (m[8] << 8));
							o->z = (short)(m[9] | (m[10] << 8)) / 10.0f;
						}
					}
					else if ((p->opcode == kNewSpawn && p->size >= 168) || p->opcode == kZoneSpawns)
					{
						// one NPC, or a batch of them (a zone that just booted fills up after we are in)
						std::vector<SpawnInfo> fresh;
						if (p->opcode == kZoneSpawns)
							fresh = DecodeSpawns(std::vector<std::vector<unsigned char> >(1, std::vector<unsigned char>(b, b + p->size)));
						else
						{
							std::vector<unsigned char> d(b, b + p->size);
							Decrypt(d, 0, 0x65e7, 9, 13, false);
							fresh.push_back(ParseSpawn(&d[4]));
						}
						for (size_t k = 0; k < fresh.size(); k++)
						{
							const SpawnInfo& si = fresh[k];
							CheckSpawn(charname, st.zone, si);
							if (si.npc != 1)
							{
								addPlayer(si);
								continue;
							}
							mobs[si.id] = Mobile{ si.name, si.level, si.x, si.y, si.z, 100, true };
							skip.erase(si.id);
							faction.erase(si.id);
						}
					}
					else if (p->opcode == (int16)0xb220 && p->size >= 12)	// SpawnHPUpdate: id, cur, max
					{
						int id = b[0] | (b[1] << 8);
						int cur = b[4] | (b[5] << 8) | (b[6] << 16) | (b[7] << 24);
						int max = b[8] | (b[9] << 8) | (b[10] << 16) | (b[11] << 24);
						if (id == st.myId && max > 0) { myHp = cur * 100 / max; myMaxHp = max; }
						else if (mobs.count(id)) mobs[id].hp = max > 0 ? cur * 100 / max : cur;
						else if (players.count(id)) players[id].hp = max > 0 ? cur * 100 / max : cur;
					}
					else if (p->opcode == (int16)0x5721 && p->size >= 4)	// Stamina: food, water (6000 full, 0 famished)
					{
						int foodLevel = (short)(b[0] | (b[1] << 8)), waterLevel = (short)(b[2] | (b[3] << 8));
						if (NowMs() - lastConsume > 3000)
						{
							if (foodLevel < 2000) consume(food, 1, "food", foodLevel);
							else if (waterLevel < 2000) consume(drink, 2, "drink", waterLevel);
						}
					}
					else if (p->opcode == (int16)0x7f21 && p->size >= 2)	// ManaChange: new mana, spell
					{
						int m = b[0] | (b[1] << 8);
						if (m > myMana) lastManaRise = NowMs();
						myMana = m;
						maxMana = std::max(maxMana, m);
					}
					else if (p->opcode == kSpecialMesg && p->size > 4 && castUntil > NowMs()
					         && (std::string((const char*)b + 4, strnlen((const char*)b + 4, p->size - 4)).find("out of range") != std::string::npos
					             || std::string((const char*)b + 4, strnlen((const char*)b + 4, p->size - 4)).find("Insufficient mana") != std::string::npos))
					{
						// the zone refused our spell: wait before trying again (a healer kept recasting)
						bool range = std::string((const char*)b + 4, strnlen((const char*)b + 4, p->size - 4)).find("range") != std::string::npos;
						castUntil = NowMs() + 8000;
						BotLog(charname, st.zone, "action=CastRefused why=%s", range ? "out_of_range" : "mana");
						if (++castRefusals >= 5)
							Anomaly(charname, st.zone, "cast_refused", "why=%s in_a_row=%d", range ? "out_of_range" : "mana", castRefusals);
					}
					else if (p->opcode == kSpecialMesg && p->size > 4 && (state == Fight || state == Approach))
					{
						// why an auto-attack swing did not go: get closer, or face the target again
						std::string text((const char*)b + 4, strnlen((const char*)b + 4, p->size - 4));
						bool far = text.find("too far away") != std::string::npos, blind = text.find("can't see your target") != std::string::npos;
						if (far) { tooFar++; closeIn = true; }
						if (blind) { cantSee++; backOff = true; }
						if ((far || blind) && NowMs() - lastMeleeMsg > 10000)
						{
							BotLog(charname, st.zone, "action=MeleeBlocked why=%s too_far=%d cant_see=%d", far ? "too_far" : "cant_see", tooFar, cantSee);
							lastMeleeMsg = NowMs();
						}
					}
					else if (p->opcode == (int16)0x4d21 && p->size >= 44)	// TeleportPC: zone[16], ..., y, x, z at 32
					{
						std::string to((const char*)b, strnlen((const char*)b, 16));
						float ty, tx, tz;
						memcpy(&ty, b + 32, 4); memcpy(&tx, b + 36, 4); memcpy(&tz, b + 40, 4);
						if (to == st.zone)
						{
							// moved within the zone (a GM summon, a teleport pad)
							meX = tx; meY = ty; meZ = tz;
							BotLog(charname, st.zone, "action=Teleported pos=%.0f,%.0f,%.0f", meX, meY, meZ);
						}
						else if (zoneLines.empty() || fabsf(zoneLines.back().first - meX) + fabsf(zoneLines.back().second - meY) > 20)
						{
							// a zone line: bots do not change zones yet, so step back and keep away from it
							// (the zone would send this at every update spent in the line)
							zoneLines.push_back(std::make_pair(meX, meY));
							BotLog(charname, st.zone, "action=ZoneLine to=%s at=%.0f,%.0f", to.c_str(), meX, meY);
							Anomaly(charname, st.zone, "zone_line", "to=%s at=%.0f,%.0f", to.c_str(), meX, meY);
							float dx = campX - meX, dy = campY - meY, d = sqrtf(dx * dx + dy * dy) + 0.01f;
							meX += dx / d * 40;
							meY += dy / d * 40;
							sendPos(campX, campY, false);
							if (state == Approach || state == Recover) { skip.insert(target); state = Seek; }
						}
					}
					else if (p->opcode == (int16)0x4020 && p->size >= 60)	// GroupInvite: invited[30], inviter[30]
					{
						std::string invited((const char*)b, strnlen((const char*)b, 30)), inviter((const char*)b + 30, strnlen((const char*)b + 30, 30));
						// join, or join again our leader (after a death, or a group refresh we misread)
						if (invited == charname && invitees.empty() && (!grouped || inviter == leaderName))
						{
							unsigned char gf[60];	// GroupFollow: leader[30], invited[30]
							memset(gf, 0, sizeof(gf));
							strncpy((char*)gf, inviter.c_str(), 29);
							strncpy((char*)gf + 30, charname.c_str(), 29);
							z.Send((int16)0x4220, gf, sizeof(gf));
							grouped = true;
							following = true;
							leaderName = inviter;
							BotLog(charname, st.zone, "action=Join leader=%s", inviter.c_str());
						}
					}
					else if (p->opcode == (int16)0x4220 && p->size >= 60)	// GroupFollow relayed to the leader
					{
						std::string joined((const char*)b + 30, strnlen((const char*)b + 30, 30));
						if (joined != charname && members.insert(joined).second)
						{
							grouped = true;
							leaderName = charname;
							BotLog(charname, st.zone, "action=Joined member=%s size=%d", joined.c_str(), (int)members.size() + 1);
						}
					}
					else if (p->opcode == (int16)0x2640 && p->size >= 225)	// GroupUpdate: receiver[32], sender[32], ..., action at 224
					{
						std::string who((const char*)b + 32, strnlen((const char*)b + 32, 32));
						int action = b[224];
						if (action == 0)
						{
							// the zone refreshes a group as "you leave" then every member again
							if (!leaderName.empty()) grouped = true;
							if (who != charname && members.insert(who).second)
								BotLog(charname, st.zone, "action=GroupMember name=%s", who.c_str());
						}
						else if (action == 3 && members.erase(who))
							BotLog(charname, st.zone, "action=GroupLeft name=%s", who.c_str());
						else if (action == 4)
						{
							BotLog(charname, st.zone, "action=GroupLeft name=%s", charname.c_str());
							grouped = false;	// the leader stays known: its next invite is taken
							members.clear();
						}
					}
					else if (p->opcode == (int16)0x9721)	// GroupDelete: the leader disbanded
					{
						BotLog(charname, st.zone, "action=GroupDisbanded");
						grouped = false;
						members.clear();
						leaderName.clear();
					}
					else if (p->opcode == kChannelMessage && p->size > 70 && b[66] != 2)	// tell, say, ooc, shout...
					{
						int chan = b[66];
						std::string from((const char*)b + 32, strnlen((const char*)b + 32, 32));
						std::string text((const char*)b + 70, strnlen((const char*)b + 70, p->size - 70));
						if (from != charname && !from.empty())
						{
							heard.push_back(from + ": " + text);
							if (heard.size() > 5) heard.erase(heard.begin());
							std::string low = text, me = charname;
							for (size_t k = 0; k < low.size(); k++) low[k] = tolower(low[k]);
							for (size_t k = 0; k < me.size(); k++) me[k] = tolower(me[k]);
							if (chan == 7)
								addressed("tell", from, text);
							else if (chan == 8 && low.find(me) != std::string::npos)
								addressed("say", from, text);
						}
					}
					else if (p->opcode == kChannelMessage && p->size > 70 && b[66] == 2)	// group say
					{
						std::string from((const char*)b + 32, strnlen((const char*)b + 32, 32));
						std::string text((const char*)b + 70, strnlen((const char*)b + 70, p->size - 70));
						BotLog(charname, st.zone, "action=Heard from=%s text=%s", from.c_str(), text.c_str());
						heard.push_back(from + ": " + text);
						if (heard.size() > 5) heard.erase(heard.begin());
						static const char* words[] = { "assist", "sit", "follow", "camp", "help", "ready" };
						bool command = text.compare(0, 3, "oom") == 0;
						for (size_t k = 0; k < sizeof(words) / sizeof(words[0]); k++)
							command = command || text == words[k];
						if (!command && from != charname)
							addressed("group", from, text);	// chatd answers players only, never bots
						if (text == "help" && !member && members.count(from) && state != Fight && state != Loot && state != Dead)
						{
							// a member is attacked: /assist it, and go for what it fights
							int mid = playerByName(from);
							if (mid)
							{
								uint16_t a[2] = { (uint16_t)mid, 0 };
								z.Send((int16)0x0022, a, sizeof(a));
								helpPending = true;
							}
						}
						else if (text.compare(0, 3, "oom") == 0) medding.insert(from);
						else if (text == "ready") medding.erase(from);
						else if (from == leaderName && member)
						{
							int lid = playerByName(leaderName);
							if (text == "assist" && lid && healGem < 0)
							{
								uint16_t a[2] = { (uint16_t)lid, 0 };	// /assist: the zone answers with the leader's target
								z.Send((int16)0x0022, a, sizeof(a));
							}
							else if (text == "sit" && (state == Seek || state == Rest))
								setState(Rest, "leader rests");
							else if (text == "follow")
							{
								following = true;
								if (state == Rest && (!myMaxHp || myHp >= 70) && !medding.count(charname))
									setState(Seek, "leader moves on");
							}
							else if (text == "camp") following = false;
						}
					}
					else if (p->opcode == (int16)0x0022 && p->size >= 2 && (member || helpPending))	// /assist answer: their target
					{
						int id = b[0] | (b[1] << 8);
						bool help = !member;
						helpPending = false;
						if (id && mobs.count(id) && mobs[id].alive && id != target && state != Dead && state != Recover && state != Loot)
						{
							if (!help) hs.assists++;
							sit(false);
							target = id;
							state = Seek;
							setState(Approach, help ? "help" : "assist");
						}
					}
					else if (p->opcode == (int16)0x3721 && p->size >= 12)	// Consider_Struct: player, target, faction
					{
						int id = b[4] | (b[5] << 8);
						int f = (int)(b[8] | (b[9] << 8) | (b[10] << 16) | ((unsigned)b[11] << 24));
						faction[id] = f;
					}
					else if (p->opcode == (int16)0x9921 && p->size >= 4)	// ExpUpdate: uint32 exp
					{
						long e = (long)(b[0] | (b[1] << 8) | (b[2] << 16) | ((unsigned long)b[3] << 24));
						expectExpBy = 0;
						if (myExp >= 0 && e != myExp)
						{
							hs.expGained += e - myExp;
							BotLog(charname, st.zone, "action=Exp exp=%ld gained=%ld", e, e - myExp);
						}
						myExp = e;
					}
					else if (p->opcode == (int16)0x5220 && p->size >= 136 && (state == Loot || state == Recover))	// ItemOnCorpse: equipSlot = loot slot
					{
						uint32_t corpse = target;
						int slot = (short)(b[134] | (b[135] << 8));
						unsigned char li[16];
						memset(li, 0, sizeof(li));
						memcpy(li, &corpse, 4);
						li[4] = st.myId & 0xff; li[5] = st.myId >> 8;
						li[8] = slot & 0xff; li[9] = slot >> 8;
						li[12] = 1;	// into the packs
						z.Send((int16)0xa020, li, sizeof(li));	// OP_LootItem
						BotLog(charname, st.zone, "action=LootItem corpse=%u item=%d slot=%d", corpse, b[130] | (b[131] << 8), slot);
						lootedItems++;
					}
					else if (p->opcode == kAction && p->size >= 16)
					{
						int to = b[0] | (b[1] << 8), from = b[4] | (b[5] << 8);
						int dmg = b[12] | (b[13] << 8) | (b[14] << 16) | (b[15] << 24);
						if (from == st.myId && to == target && dmg > 0) { hs.hitsDealt++; hs.damageDealt += dmg; fightHits++; lastHitDealt = NowMs(); headingOffset = 0; }
						if (from != st.myId && players.count(from) && mobs.count(to))
							mobs[to].touched = true;
						if (to == target && from != st.myId && players.count(from))
							sharedTarget = true;	// another player hit it: the experience may go to them
						if (to == st.myId && from != st.myId && mobs.count(from))
						{
							hs.hitsTaken++;
							lastHitTaken = NowMs();
							if (dmg > 0) hs.damageTaken += dmg;
							// someone is on us: fight back, whatever we were doing (but running away)
							if (from != target && state != Fight && state != Flee && state != Dead)
							{
								if (attackers.insert(from).second)
								{
									BotLog(charname, st.zone, "action=Attacked by=%s(%d) hp=%d%%", mobs[from].name.c_str(), from, myHp);
									if (member && grouped)
										gsay("help");
								}
								sit(false);
								target = from;
								state = Seek;	// so that the change is logged
								if (!member && isDanger(from))
								{
									// a named far above us: no fight to win, run to the camp (its guards), or
									// straight away from it when we are already there
									fleeX = campX; fleeY = campY;
									if (fabsf(meX - campX) + fabsf(meY - campY) < 40)
									{
										float dx = meX - mobs[from].x, dy = meY - mobs[from].y, d = sqrtf(dx * dx + dy * dy) + 0.01f;
										fleeX = meX + dx / d * 150; fleeY = meY + dy / d * 150;
									}
									if (nearZoneLine(fleeX, fleeY)) { fleeX = campX; fleeY = campY; }
									attack(false);
									setState(Flee, "attacked by a named");
								}
								else
									setState(Approach, "attacked");
							}
						}
					}
					else if (p->opcode == kDeath && p->size >= 8)
					{
						int id = b[0] | (b[1] << 8), killer = b[4] | (b[5] << 8);
						if (id == st.myId)
						{
							hs.deaths++;
							if (!recovering.empty())
							{
								// killed while going back to a corpse: something camps it, give it up
								BotLog(charname, st.zone, "action=AbandonCorpse corpse=%s", recovering.c_str());
								hs.emptiedCorpses.insert(recovering);
							}
							if (!food.empty() || !drink.empty())
							{
								hs.corpseFood = food;	// what we carried stays on the corpse
								hs.corpseDrink = drink;
							}
							BotLog(charname, st.zone, "action=Killed by=%s(%d)", mobs.count(killer) ? mobs[killer].name.c_str() : "?", killer);
							hs.sayDeath = true;
							setState(Dead, "killed");
							died = true;
						}
						else if (mobs.count(id))
						{
							mobs[id].alive = false;
							if (id == target && state != Dead && member)
							{
								// the leader loots; a member goes back to following
								BotLog(charname, st.zone, "action=Kill target=%s(%d) level=%d hits=%d hp=%d%% assist=1", mobs[id].name.c_str(), id, mobs[id].level, fightHits, myHp);
								attack(false);
								mobs.erase(id);
								setState(Seek, "assist done");
								target = 0;
							}
							else if (id == target && state != Dead)
							{
								hs.kills++;
								// ours (the death names us) and not green: experience must follow
								// another player standing by it was on it too (two bots often pick the same closest
								// prey): the zone gives the experience to whoever did the most damage, as in classic
								bool playerNear = false;
								for (std::map<int, Mobile>::iterator pl = players.begin(); pl != players.end() && !playerNear; ++pl)
									playerNear = pl->first != st.myId && pl->second.alive &&
										fabsf(pl->second.x - mobs[id].x) < 25 && fabsf(pl->second.y - mobs[id].y) < 25;
								if (killer == st.myId && !sharedTarget && !playerNear && mobs[id].level >= myLevel - 2 && myLevel < 50)
								{
									expectExpBy = NowMs() + 5000;
									expectExpFrom = mobs[id].name;
									expectExpLevel = mobs[id].level;
								}
								BotLog(charname, st.zone, "action=Kill target=%s(%d) level=%d hits=%d hp=%d%%", mobs[id].name.c_str(), id, mobs[id].level, fightHits, myHp);
								attack(false);
								uint32_t corpse = target;	// an NPC's corpse keeps its id
								z.Send(0x4e20, &corpse, sizeof(corpse));	// OP_LootRequest
								lootedItems = 0;
								setState(Loot, "it died");
							}
						}
					}
					else if (p->opcode == (int16)0xf520 && p->size >= 12 && (b[4] | (b[5] << 8)) == 16 && st.myId == 0)
						st.myId = b[8] | (b[9] << 8);
				}
				DeleteAll(got);
			}
			long now = NowMs();
			float step = speed * (now - lastTick) / 1000.0f;
			lastTick = now;
			bool underAttack = now - lastHitTaken < 6000;
			if (expectExpBy && now > expectExpBy)
			{
				Anomaly(charname, st.zone, "exp_missing", "kill=%s level=%d my_level=%d grouped=%d", expectExpFrom.c_str(), expectExpLevel, myLevel, grouped ? 1 : 0);
				expectExpBy = 0;
			}
			if (now - lastPacket > 30000 && state != Dead && !silentSince)
			{
				Anomaly(charname, st.zone, "zone_silent", "seconds=%ld state=%s pending=%d our_side=%s", (now - lastPacket) / 1000, names[state],
					z.Pending(), z.Active() ? "up" : "gave_up");
				if (getenv("EQBOT_NETDEBUG"))
					z.DumpRecent();
				silentSince = lastPacket;
			}
			if (silentSince && lastPacket > silentSince)
				silentSince = 0;	// it talks again
			if (silentSince && now - silentSince > 60000 && state != Dead)
			{
				// The zone dropped us (it does after 15 resends of a packet we did not ack) and says nothing
				// more: log in again rather than walk alone for hours
				Anomaly(charname, st.zone, "session_lost", "silent_seconds=%ld state=%s", (now - silentSince) / 1000, names[state]);
				relog = true;
				state = Dead;
			}
			// chat: an answer from chatd goes out where it was asked for
			ChatLine said;
			if (chat.Take(said) && !said.text.empty() && state != Dead)
			{
				if (said.channel == "tell") SendChat(z, charname, 7, said.to, said.text);
				else if (said.channel == "say") SendChat(z, charname, 8, "", said.text);
				else if (said.channel == "ooc") SendChat(z, charname, 5, "", said.text);
				else if (said.channel == "group") gsay(said.text);
				BotLog(charname, st.zone, "action=Chat channel=%s to=%s source=%s ms=%ld text=%s", said.channel.c_str(),
				       said.to.empty() ? "-" : said.to.c_str(), said.source.c_str(), now - said.asked, said.text.c_str());
			}
			// EQBOT_LFG: a bot alone looks for a group on OOC (chatd keeps it to one line per interval)
			if (getenv("EQBOT_LFG") && !grouped && invitees.empty() && now - lastLfg > 60000 && state != Dead)
			{
				if (askLine("lfg", "ooc", ""))
					lastLfg = now;
			}
			if (hs.sayDeath && grouped && state != Dead && askLine("death", "group", ""))
				hs.sayDeath = false;
			// leader: invite who is missing (again after a death or a disconnect)
			for (size_t k = 0; k < invitees.size(); k++)
			{
				const std::string& n = invitees[k];
				int id = playerByName(n);
				if (!id || members.count(n) || now - lastInvite[n] < 20000 || state == Dead)
					continue;
				lastInvite[n] = now;
				uint16_t t[2] = { (uint16_t)id, 0 };	// the zone sends the invite to our target
				z.Send(kClientTarget, t, sizeof(t));
				unsigned char gi[91];	// GroupInvite: invited[30], inviter[30], unknown[31]
				memset(gi, 0, sizeof(gi));
				strncpy((char*)gi, n.c_str(), 29);
				strncpy((char*)gi + 30, charname.c_str(), 29);
				z.Send((int16)0x4020, gi, sizeof(gi));
				BotLog(charname, st.zone, "action=Invite name=%s", n.c_str());
			}
			// healer: the lowest group member under 60 %, in range
			if (healGem >= 0 && grouped && state != Dead && state != Recover && now >= castUntil)
			{
				int low = myMaxHp ? myHp : 100, lowId = st.myId;
				std::string lowName = charname;
				std::set<std::string> group = members;
				if (!leaderName.empty()) group.insert(leaderName);
				for (std::set<std::string>::iterator it = group.begin(); it != group.end(); ++it)
				{
					int id = playerByName(*it);
					if (!id) continue;
					const Mobile& o = players[id];
					float d = sqrtf((o.x - meX) * (o.x - meX) + (o.y - meY) * (o.y - meY));
					if (d < 100 && o.hp < low) { low = o.hp; lowId = id; lowName = *it; }
				}
				if (low < 60)
					cast(healGem, kMinorHealing, kMinorHealingMana, lowId, "Minor_Healing", lowName, "group member hurt");
			}
			// a healer in a group tells when it needs to meditate, and when it is back (the group waits
			// for its healer; a nuker out of mana just stops nuking)
			if (healGem >= 0 && grouped && maxMana > 0)
			{
				bool oom = medding.count(charname) > 0;
				if (!oom && myMana * 100 < maxMana * 30) { medding.insert(charname); gsay("oom, medding"); }
				else if (oom && myMana * 100 >= maxMana * 90) { medding.erase(charname); gsay("ready"); }
			}
			// a dangerous NPC roams close (3 levels above us or more, not a guard, not known friendly):
			// step away before it notices us, as a player keeping an eye on a named that wanders
			bool avoided = false;
			if (!member && !underAttack && (state == Seek || state == Rest))
			{
				int dangerId = 0;
				float nearest = 70;
				for (std::map<int, Mobile>::iterator it = mobs.begin(); it != mobs.end(); ++it)
				{
					float d = sqrtf((it->second.x - meX) * (it->second.x - meX) + (it->second.y - meY) * (it->second.y - meY));
					if (d < nearest && isDanger(it->first)) { nearest = d; dangerId = it->first; }
				}
				if (dangerId)
				{
					const Mobile& m = mobs[dangerId];
					float ax = meX - m.x, ay = meY - m.y, len = sqrtf(ax * ax + ay * ay);
					if (len < 1) { ax = meX - campX; ay = meY - campY; len = sqrtf(ax * ax + ay * ay); }
					if (len < 1) { ax = 1; ay = 0; len = 1; }
					float tx = meX + ax / len * 40, ty = meY + ay / len * 40;
					if (nearZoneLine(tx, ty)) { tx = campX; ty = campY; }
					if (now - lastAvoid > 10000)
					{
						BotLog(charname, st.zone, "action=Avoid target=%s(%d) level=%d dist=%.0f", m.name.c_str(), dangerId, m.level, nearest);
						lastAvoid = now;
					}
					sit(false);
					walkTo(tx, ty, meZ, step, 0);
					if (state == Rest)
						setState(Seek, "danger near");
					avoided = true;
				}
			}
			// EQBOT_TOWN_EVERY=<minutes>: a trip now and then to empty the packs, hungry or not
			if (!hs.wantTown && getenv("EQBOT_TOWN") && getenv("EQBOT_TOWN_EVERY") && now - std::max(hs.lastTown, start) > atol(getenv("EQBOT_TOWN_EVERY")) * 60000L)
				hs.wantTown = true;
			if (hs.wantTown && !member && !underAttack && state == Seek && myMaxHp && myHp < 90)
				setState(Rest, "before the trip to town");	// the road is walked without fighting back: leave healthy
			if (hs.wantTown && !member && !underAttack && (state == Seek || state == Rest) && (!myMaxHp || myHp >= 90))
			{
				BotLog(charname, st.zone, "action=LeaveForTown hp=%d%% pos=%.0f,%.0f", myHp, meX, meY);
				sit(false);
				break;
			}
			if (!avoided) switch (state)
			{
			case Seek:
			{
				target = 0;
				if (myMaxHp && myHp < 90 && !underAttack)	// a fight starts healthy
				{
					setState(Rest, "hurt");
					break;
				}
				if (myMaxHp && myHp < 50 && underAttack && !attackers.empty())
					break;	// hurt and hit: the attacker gets fought back (above), no new prey on top of it
				float best = 1e30f;
				int corpseId = 0;
				for (std::map<int, Mobile>::iterator it = myCorpses.begin(); it != myCorpses.end() && !corpseId; ++it)
				{
					// grouped, only a corpse near the leader: nobody goes alone across the zone
					int lid = member && grouped ? playerByName(leaderName) : 0;
					if ((!lid || fabsf(players[lid].x - it->second.x) + fabsf(players[lid].y - it->second.y) < 150) &&
					    !nearDanger(it->second.x, it->second.y))	// not while what killed us stands by it
						corpseId = it->first;
				}
				if (corpseId && !underAttack)
				{
					target = corpseId;
					recovering = myCorpses[target].name;
					lootedItems = 0;
					setState(Recover, "our corpse is here");
					break;
				}
				if (member)
				{
					// never pull: follow the leader (or stay, after "camp")
					int lid = grouped ? playerByName(leaderName) : 0;
					if (lid && following)
						walkTo(players[lid].x, players[lid].y, players[lid].z, step, 8);
					else if (!lid && now - lastNoPrey > 30000)
					{
						BotLog(charname, st.zone, "action=NoLeader grouped=%d", grouped ? 1 : 0);
						lastNoPrey = now;
					}
					if (medding.count(charname) && !underAttack)
						setState(Rest, "oom");
					break;
				}
				if (!invitees.empty())
				{
					// pull only with the whole group near, healthy and with mana
					std::string why;
					for (size_t k = 0; k < invitees.size() && why.empty(); k++)
					{
						int id = playerByName(invitees[k]);
						if (!id || !members.count(invitees[k])) why = invitees[k] + "_not_in_group";
						else if (sqrtf((players[id].x - meX) * (players[id].x - meX) + (players[id].y - meY) * (players[id].y - meY)) > 60) why = invitees[k] + "_far";
						else if (players[id].hp < 80) why = invitees[k] + "_hurt";
					}
					if (why.empty() && !medding.empty()) why = *medding.begin() + "_medding";
					if (why.empty())
						waitSince = 0;
					else if (!waitSince)
						waitSince = now;
					if (!why.empty() && now - waitSince < 180000)	// 3 minutes at most, then hunt anyway
					{
						if (now - lastNoPrey > 30000) { BotLog(charname, st.zone, "action=Wait why=%s", why.c_str()); lastNoPrey = now; }
						break;
					}
				}
				int maxPrey = myLevel + (members.empty() ? 0 : 2);	// white and below; a group takes yellow too
				for (std::map<int, Mobile>::iterator it = mobs.begin(); it != mobs.end(); ++it)
				{
					const Mobile& m = it->second;
					if (!m.alive || m.hp < 100 || skip.count(it->first) || m.level > maxPrey || !IsPreyName(m.name))
						continue;
					if (nearDanger(m.x, m.y))
						continue;	// it would bring us next to a named that kills us
					float fromCamp = sqrtf((m.x - campX) * (m.x - campX) + (m.y - campY) * (m.y - campY));
					if (fromCamp > campRadius || nearZoneLine(m.x, m.y))
						continue;
					// close to us and to the camp (near the zone-in, guards help against what roams)
					float d = sqrtf((m.x - meX) * (m.x - meX) + (m.y - meY) * (m.y - meY)) + fromCamp;
					if (d < best) { best = d; target = it->first; }
				}
				if (target)
				{
					sit(false);
					if (faction.count(target))
						setState(Approach, "closest prey");
					else
					{
						unsigned char c[28];	// Consider_Struct: the zone drops any other size
						memset(c, 0, sizeof(c));
						c[0] = st.myId & 0xff; c[1] = st.myId >> 8;
						c[4] = target & 0xff; c[5] = target >> 8;
						z.Send((int16)0x3721, c, sizeof(c));
						setState(Consider, "closest prey");
					}
				}
				else
				{
					// nothing to hunt: stroll around the camp, as a player waiting for a respawn
					if (now - lastNoPrey > 30000 || walkTo(roamX, roamY, campZ, step * 0.5f, 3))
					{
						float a = (rand() % 360) * 3.14159f / 180, r = 30 + rand() % 120;
						roamX = campX + r * cosf(a);
						roamY = campY + r * sinf(a);
						if (nearZoneLine(roamX, roamY)) { roamX = campX; roamY = campY; }
						if (now - lastNoPrey > 30000)
							BotLog(charname, st.zone, "action=NoPrey roam=%.0f,%.0f", roamX, roamY);
						lastNoPrey = now;
					}
				}
				break;
			}
			case Consider:
				if (faction.count(target))
				{
					int f = faction[target];
					if (f > 0)	// amiable or better (0x100 amiable ... 0x500 ally); 0 is indifferent
					{
						hs.refused++;
						skip.insert(target);
						BotLog(charname, st.zone, "action=Refuse target=%s(%d) faction=%d", mobs[target].name.c_str(), target, f);
						setState(Seek, "cons amiable or better");
					}
					else
						setState(Approach, "cons indifferent or worse");
				}
				else if (now - stateSince > 3000)
				{
					skip.insert(target);
					Anomaly(charname, st.zone, "consider_timeout", "target=%s(%d) level=%d", mobs[target].name.c_str(), target, mobs[target].level);
					setState(Seek, "no consider answer");
				}
				break;
			case Approach:
			case Fight:
			{
				if (!mobs.count(target) || !mobs[target].alive) { attack(false); setState(Seek, "prey gone"); break; }
				Mobile& m = mobs[target];
				if (state == Approach && now - stateSince > 60000)
				{
					Anomaly(charname, st.zone, "unreachable", "target=%s(%d) at=%.0f,%.0f me=%.0f,%.0f", m.name.c_str(), target, m.x, m.y, meX, meY);
					skip.insert(target); setState(Seek, "too long to reach"); break;
				}
				if (state == Fight && now - std::max(stateSince, lastHitDealt) > 30000 && !(member && nukeGem >= 0))
				{
					Anomaly(charname, st.zone, "no_hit", "target=%s(%d) level=%d dist=%.1f too_far=%d cant_see=%d",
					        m.name.c_str(), target, m.level, sqrtf((m.x - meX) * (m.x - meX) + (m.y - meY) * (m.y - meY)), tooFar, cantSee);
					attack(false); skip.insert(target); setState(Seek, "no hit in 30 s"); break;
				}
				if (member && nukeGem >= 0)
				{
					// a wizard stays back and nukes, once the tank holds the mob (hurt, or 5 s into the fight)
					if (walkTo(m.x, m.y, m.z, step, 30))
					{
						if (state == Approach) { fightHits = 0; setState(Fight, "in range"); }
						if (m.hp < 95 || now - stateSince > 5000)
							cast(nukeGem, kFrostBolt, kFrostBoltMana, target, "Frost_Bolt", m.name, "assist");
					}
					break;
				}
				if (state == Fight && !members.empty() && now - lastAssistCall > 10000)
				{
					gsay("assist");
					lastAssistCall = now;
				}
				if (state == Fight && myMaxHp && myHp < 50 && castHeal("fighting"))
					break;
				if (state == Fight && myHp < 20 && m.hp > 50 && myMaxHp && !fledThisFight)
				{
					float dx = meX - m.x, dy = meY - m.y, d = sqrtf(dx * dx + dy * dy) + 0.01f;
					fleeX = meX + dx / d * 150;
					fleeY = meY + dy / d * 150;
					fledThisFight = true;
					attack(false);
					setState(Flee, "losing");
					break;
				}
				if (closeIn)
				{
					// the zone says we are out of reach: step closer
					closeIn = false;
					walkTo(m.x, m.y, m.z, step, 1.5f);
					break;
				}
				if (backOff)
				{
					// the zone says we do not face it. Its position we know may be old (the zone sends
					// an NPC's moves rarely, a client extrapolates them): turn a quarter more each time,
					// and step back first if we stand right on it
					backOff = false;
					float dx = meX - m.x, dy = meY - m.y, d = sqrtf(dx * dx + dy * dy);
					if (d < 1)
					{
						if (d < 0.01f) { dx = 1; dy = 0; d = 1; }
						meX = m.x + dx / d * 3;
						meY = m.y + dy / d * 3;
					}
					else
						headingOffset += 64;
					sendPos(m.x, m.y, false);
					break;
				}
				if (walkTo(m.x, m.y, m.z, step, 2) && state == Approach)
				{
					uint16_t t[2] = { (uint16_t)target, 0 };
					z.Send(kClientTarget, t, sizeof(t));
					attack(true);
					fightHits = 0;
					headingOffset = 0;
					// hurt before we came (another bot hit it, then fled or died): whoever did the most damage
					// is still on its hate list and gets the experience, as in classic EQ
					sharedTarget = mobs[target].touched || mobs[target].hp < 100;
					fledThisFight = false;
					setState(Fight, "in reach");
					waitSince = 0;
					if (!members.empty())
					{
						gsay("assist");
						lastAssistCall = NowMs();
						askLine("inc", "group", ",\"mob\":\"" + JsonEscape(m.name) + "\"");
					}
				}
				break;
			}
			case Loot:
				if (now - stateSince > 2000)	// the items came right after the request
				{
					uint32_t corpse = target;
					z.Send(0x4f20, &corpse, sizeof(corpse));	// OP_EndLootRequest
					hs.loots++;
					hs.items += lootedItems;
					mobs.erase(target);
					setState(Rest, lootedItems ? "looted" : "nothing to loot");
					if (!members.empty()) gsay("sit");
					target = 0;	// its id comes back with the next spawn: that one is not our prey
				}
				break;
			case Rest:
			{
				if (member && grouped && following && !underAttack && !medding.count(charname))
				{
					// do not stay behind: the leader went on
					int lid = playerByName(leaderName);
					if (lid && sqrtf((players[lid].x - meX) * (players[lid].x - meX) + (players[lid].y - meY) * (players[lid].y - meY)) > 40)
					{
						sit(false);
						setState(Seek, "leader left");
						break;
					}
				}
				if (myMaxHp && myHp < 70 && !sitting && castHeal("resting"))
					break;
				if (now < castUntil)
					break;
				sit(true);	// sitting is also how a caster meditates
				bool healed = myMaxHp ? myHp >= 95 : now - stateSince > 20000;
				// a caster waits for its mana too, while it still comes back
				if ((healGem >= 0 || nukeGem >= 0) && myMana < maxMana && now - std::max(lastManaRise, stateSince) < 30000)
					healed = false;
				if (medding.count(charname))
					healed = false;	// said "oom": rest until "ready"
				if ((healed && now - stateSince > 5000) || now - stateSince > 180000)
				{
					sit(false);
					setState(Seek, healed ? "rested" : "rested long enough");
					if (!members.empty()) gsay("follow");
				}
				break;
			}
			case Flee:
				// straight away from what beats us, then rest; one try per fight
				if (walkTo(fleeX, fleeY, meZ, step * 1.5f, 5) || now - stateSince > 20000)
					setState(underAttack ? Seek : Rest, underAttack ? "could not get away" : "got away");
				break;
			case Recover:
			{
				Mobile& c = myCorpses[target];
				if (nearZoneLine(c.x, c.y)) { hs.emptiedCorpses.insert(c.name); myCorpses.erase(target); recovering.clear(); setState(Seek, "corpse by a zone line"); break; }
				if (now - stateSince > 120000) { hs.emptiedCorpses.insert(c.name); myCorpses.erase(target); setState(Seek, "corpse out of reach"); break; }
				if (!walkTo(c.x, c.y, c.z, step, 2))
					break;
				if (!c.alive)	// request sent: the items came right after it
				{
					if (now - recoverSentAt > 3000)
					{
						uint32_t corpse = target;
						z.Send(0x4f20, &corpse, sizeof(corpse));	// OP_EndLootRequest
						BotLog(charname, st.zone, "action=Recovered corpse=%s items=%d", c.name.c_str(), lootedItems);
						if (lootedItems && (!hs.corpseFood.empty() || !hs.corpseDrink.empty()))
						{
							// the rations came back with the rest (the inventory packet is only sent at zone-in)
							food = hs.corpseFood; drink = hs.corpseDrink;
							hs.corpseFood.clear(); hs.corpseDrink.clear();
							saidHungry = false;
						}
						hs.recovered++;
						hs.emptiedCorpses.insert(c.name);
						recovering.clear();
						myCorpses.erase(target);
						target = 0;
						// the zone puts worn items back in their slots; what went to the packs is put on
						// at the next login (logging in again now would be refused for a minute)
						setState(Seek, lootedItems ? "corpse looted" : "corpse empty");
					}
					break;
				}
				uint32_t corpse = target;
				z.Send(0x4e20, &corpse, sizeof(corpse));	// OP_LootRequest
				c.alive = false;	// asked
				recoverSentAt = now;
				break;
			}
			case Dead:
				break;
			}
			if (now - lastStatus >= 30000)
			{
				const char* label = state == Seek && member ? "Follow" : names[state];
				BotLog(charname, st.zone, "action=Status state=%s hp=%d%% mana=%d kills=%d assists=%d loots=%d items=%d exp=%ld deaths=%d group=%d pos=%.0f,%.0f,%.0f",
				       label, myHp, myMana, hs.kills, hs.assists, hs.loots, hs.items, hs.expGained, hs.deaths, grouped || !members.empty() ? (int)members.size() + 1 : 0, meX, meY, meZ);
				lastStatus = now;
			}
		}
		if (state != Dead)
			attack(false);
		z.Disconnect();
		return true;
	}

	bool TownTrip(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname,
	              const std::string& town, const std::string& linesFile);

	int Hunt(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname, int seconds)
	{
		long start = NowMs(), deadline = start + seconds * 1000L;
		HuntStats hs;
		int lives = 0;
		std::vector<long> deaths;
		while (NowMs() < deadline)
		{
			bool died = false, relog = false, in = false;
			for (int attempt = 0; attempt < 8 && !in && NowMs() < deadline; attempt++)
			{
				// World refuses a login for about a minute after the last one ended ("active character")
				if (attempt)
				{
					BotLog(charname, "-", "action=LoginRetry attempt=%d wait=15", attempt);
					if (attempt >= 3)
						Anomaly(charname, "-", "login_refused", "attempts=%d", attempt);
					long until = NowMs() + 15000;
					while (NowMs() < until && NowMs() < deadline)
					{
						struct timeval tv = { 0, 200000 };
						select(0, 0, 0, 0, &tv);
					}
				}
				in = HuntLife(host, user, pass, charname, deadline, hs, died, relog);
			}
			if (!in)
				break;
			lives++;
			if (relog)
			{
				BotLog(charname, "-", "action=Relog why=equip");
				continue;
			}
			if (!died && hs.wantTown && NowMs() < deadline)
			{
				// milestone 5: to town and back, then a new life at the camp
				hs.wantTown = false;
				hs.lastTown = NowMs();
				hs.townTrips++;
				TownTrip(host, user, pass, charname, getenv("EQBOT_TOWN"), getenv("EQBOT_ZONELINES") ? getenv("EQBOT_ZONELINES") : "../botd/paths/zonelines.tsv");
				continue;
			}
			if (!died)
				break;
			deaths.push_back(NowMs());
			while (!deaths.empty() && NowMs() - deaths.front() > 600000) deaths.erase(deaths.begin());
			if (deaths.size() >= 3)
				Anomaly(charname, "-", "death_loop", "deaths_in_10_min=%d", (int)deaths.size());
			// the zone moves the dead to their bind point; a new login finds them there
			BotLog(charname, "-", "action=Respawn wait=15");
			long until = NowMs() + 15000;
			while (NowMs() < until && NowMs() < deadline)
			{
				struct timeval tv = { 0, 200000 };
				select(0, 0, 0, 0, &tv);
			}
		}
		BotLog(charname, "-", "action=Done kills=%d assists=%d casts=%d loots=%d items=%d exp=%ld deaths=%d recovered=%d equipped=%d refused=%d hits=%d/%d dmg=%d/%d lives=%d seconds=%.0f",
		       hs.kills, hs.assists, hs.casts, hs.loots, hs.items, hs.expGained, hs.deaths, hs.recovered, hs.equipped, hs.refused, hs.hitsDealt, hs.hitsTaken, hs.damageDealt, hs.damageTaken, lives,
		       (NowMs() - start) / 1000.0);
		return lives ? 0 : 1;
	}

	// ---- cast: one spell from a gem on ourselves; the zone casts it (OP_ManaChange naming the spell,
	// OP_BeginCast) or refuses ----
	int Cast(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname, int gem, int spell)
	{
		EqSession z;
		ZoneState st;
		if (!EnterZone(host, user, pass, charname, z, st))
			return 1;
		std::vector<Packet*> early = z.Poll(1500);
		KeepSpawnPackets(st, early);
		DeleteAll(early);
		unsigned char c[16];	// CastSpell_Struct: slot, spell, inventory slot (0xffff: a gem), target
		memset(c, 0, sizeof(c));
		c[0] = gem;
		c[2] = spell & 0xff; c[3] = spell >> 8;
		c[4] = 0xff; c[5] = 0xff;
		c[8] = st.myId & 0xff; c[9] = st.myId >> 8;
		z.Send((int16)0x7e21, c, sizeof(c));
		std::vector<Packet*> got = Collect(z, 4000, [](const std::vector<Packet*>&) { return false; });
		bool began = false;
		for (size_t i = 0; i < got.size(); i++)
		{
			const unsigned char* b = got[i]->pBuffer;
			if (got[i]->opcode == (int16)0xa920 && got[i]->size >= 6)	// BeginCast_Struct: caster id, spell id
				began = began || (b[4] | (b[5] << 8)) == spell;
			// ManaChange_Struct (new mana, spell): the caster's own sign that the spell went
			if (got[i]->opcode == (int16)0x7f21 && got[i]->size >= 4)
			{
				if (getenv("EQBOT_VERBOSE")) printf("       mana %d spell %d\n", b[0] | (b[1] << 8), b[2] | (b[3] << 8));
				began = began || (b[2] | (b[3] << 8)) == spell;
			}
		}
		for (size_t i = 0; i < got.size() && getenv("EQBOT_VERBOSE"); i++)
			if (got[i]->opcode != kMobUpdate)
				printf("       0x%04x %d bytes\n", (unsigned)(unsigned short)got[i]->opcode, (int)got[i]->size);
		std::vector<std::string> texts = Texts(got);
		for (size_t i = 0; i < texts.size(); i++)
			printf("       zone says: %s\n", texts[i].c_str());
		DeleteAll(got);
		char d[64];
		snprintf(d, sizeof(d), "spell %d from gem %d: %s", spell, gem, began ? "cast" : "not cast");
		Step(true, "cast", d);
		printf("CAST %s\n", began ? "started" : "refused");
		z.Disconnect();
		return 0;
	}

	// ---- tell: send a tell to a character and wait for its answer (milestone 4 check) ----
	int Tell(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname,
	         const std::string& to, const std::string& text, int waitSec)
	{
		EqSession z;
		ZoneState st;
		if (!EnterZone(host, user, pass, charname, z, st))
			return 1;
		std::vector<Packet*> early = z.Poll(1500);
		DeleteAll(early);
		long sent = NowMs();
		SendChat(z, charname, 7, to, text);
		std::string answer;
		while (answer.empty() && NowMs() - sent < waitSec * 1000L)
		{
			std::vector<Packet*> got = z.Poll(200);
			for (size_t i = 0; i < got.size(); i++)
			{
				const unsigned char* b = got[i]->pBuffer;
				if (got[i]->opcode == kChannelMessage && got[i]->size > 70 && b[66] == 7
				    && std::string((const char*)b + 32, strnlen((const char*)b + 32, 32)) == to)
					answer = std::string((const char*)b + 70, strnlen((const char*)b + 70, got[i]->size - 70));
			}
			DeleteAll(got);
		}
		char d[200];
		snprintf(d, sizeof(d), "%s answered after %ld ms: %s", to.c_str(), NowMs() - sent, answer.c_str());
		Step(!answer.empty(), "tell", answer.empty() ? to + ": no answer" : d);
		z.Disconnect();
		return answer.empty() ? 1 : 0;
	}

	// ---- travel: bots change zones (milestone 6) ----
	// Zone lines come from tools/botd/paths/zonelines.tsv (zonelines.sh): a point and its range, or an X/Y
	// line. The bot walks to the next zone's line; the zone answers OP_TeleportPC (another zone), the bot
	// asks OP_ZoneChange, the zone confirms with OP_ZoneChange and the zone's name, the bot goes back to
	// World with its login ticket, enters the world again and lands in the new zone.

	struct ZoneLine { std::string zone, target; float x, y, z, range, minv, maxv; int mode; };

	std::vector<ZoneLine> LoadZoneLines(const std::string& path)
	{
		std::vector<ZoneLine> out;
		FILE* f = fopen(path.c_str(), "r");
		if (!f) return out;
		char zone[64], target[64];
		ZoneLine l;
		while (fscanf(f, "%63s %f %f %f %f %d %f %f %63s", zone, &l.x, &l.y, &l.z, &l.range, &l.mode, &l.minv, &l.maxv, target) == 9)
		{
			l.zone = zone;
			l.target = target;
			out.push_back(l);
		}
		fclose(f);
		return out;
	}

	// Where to stand to cross a line, from where we are.
	void ZoneLineGoal(const ZoneLine& l, float meX, float meY, float& gx, float& gy)
	{
		float lo = l.minv ? l.minv + 5 : -1e9f, hi = l.maxv ? l.maxv - 5 : 1e9f;
		if (l.mode == 1)	// X line: x past the trigger, y within the bounds
		{
			gx = l.x + (l.x >= 0 ? 10 : -10);
			gy = std::min(std::max(meY, lo), hi);
		}
		else if (l.mode == 2)	// Y line
		{
			gy = l.y + (l.y >= 0 ? 10 : -10);
			gx = std::min(std::max(meX, lo), hi);
		}
		else
		{
			gx = l.x;
			gy = l.y;
		}
	}

	const int16 kZoneChange = (int16)0xa320;	// ZoneChange_Struct: char name[32], zone name[16], 20 bytes

	int TravelLegs(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname,
	               EqSession*& z, ZoneState& st, const std::vector<std::string>& legs, const std::vector<ZoneLine>& lines);

	int Travel(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname,
	           const std::string& route, const std::string& linesFile)
	{
		std::vector<ZoneLine> lines = LoadZoneLines(linesFile);
		if (lines.empty()) { Step(false, "zone lines", "none in " + linesFile); return 1; }
		std::vector<std::string> legs;
		for (size_t a = 0, b; a < route.size(); a = b + 1)
		{
			b = route.find(',', a);
			if (b == std::string::npos) b = route.size();
			if (b > a) legs.push_back(route.substr(a, b - a));
		}
		EqSession* z = new EqSession();
		ZoneState st;
		if (!EnterZone(host, user, pass, charname, *z, st)) { BotLog(charname, "-", "action=Login result=failed"); return 1; }
		int ok = TravelLegs(host, user, pass, charname, z, st, legs, lines);
		BotLog(charname, st.zone, "action=Done legs=%d of=%d", ok, (int)legs.size());
		z->Disconnect();
		delete z;
		return ok == (int)legs.size() ? 0 : 1;
	}

	// Walks to the zone line of each leg and changes zone; the session `z` and `st` are those of the last
	// zone reached. Returns the number of legs done.
	int TravelLegs(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname,
	               EqSession*& z, ZoneState& st, const std::vector<std::string>& legs, const std::vector<ZoneLine>& lines)
	{
		int ok = 0;
		for (size_t leg = 0; leg < legs.size(); leg++)
		{
			const std::string& to = legs[leg];
			std::vector<unsigned char> pp = DecodeProfile(st.profile);
			float meX = 0, meY = 0, meZ = 0;
			if (pp.size() >= 2420) { memcpy(&meY, &pp[2408], 4); memcpy(&meX, &pp[2412], 4); memcpy(&meZ, &pp[2416], 4); }
			int myId = st.myId;
			const ZoneLine* line = 0;
			float best = 1e30f;
			for (size_t i = 0; i < lines.size(); i++)
				if (lines[i].zone == st.zone && lines[i].target == to)
				{
					float gx, gy;
					ZoneLineGoal(lines[i], meX, meY, gx, gy);
					float d = (gx - meX) * (gx - meX) + (gy - meY) * (gy - meY);
					if (d < best) { best = d; line = &lines[i]; }
				}
			if (!line)
			{
				BotLog(charname, st.zone, "action=Travel result=no_line to=%s", to.c_str());
				break;
			}
			float gx, gy;
			ZoneLineGoal(*line, meX, meY, gx, gy);
			BotLog(charname, st.zone, "action=TravelTo to=%s line=%.0f,%.0f goal=%.0f,%.0f from=%.0f,%.0f dist=%.0f", to.c_str(), line->x, line->y,
			       gx, gy, meX, meY, sqrtf(best));
			long start = NowMs(), last = start, asked = 0;
			unsigned tick = 0;
			std::string confirmed;
			bool teleported = false;
			bool killed = false;
			while (NowMs() - start < 600000 && confirmed.empty() && !killed)
			{
				std::vector<Packet*> got = z->Poll(250);
				for (size_t i = 0; i < got.size(); i++)
				{
					const Packet* p = got[i];
					const unsigned char* b = p->pBuffer;
					if (p->opcode == (int16)0xf520 && p->size >= 12 && (b[4] | (b[5] << 8)) == 16 && myId == 0)
						myId = b[8] | (b[9] << 8);
					else if (p->opcode == kDeath && p->size >= 8 && myId && (b[0] | (b[1] << 8)) == myId)
					{
						BotLog(charname, st.zone, "action=Killed on_the_road=1 to=%s at=%.0f,%.0f", to.c_str(), meX, meY);
						killed = true;
					}
					else if (p->opcode == (int16)0x4d21 && p->size >= 44)	// TeleportPC: the zone sends us elsewhere
					{
						std::string dest((const char*)b, strnlen((const char*)b, 16));
						if (dest != st.zone && !teleported)
						{
							teleported = true;
							BotLog(charname, st.zone, "action=ZoneLine to=%s at=%.0f,%.0f", dest.c_str(), meX, meY);
							unsigned char zc[68];
							memset(zc, 0, sizeof(zc));
							strncpy((char*)zc, charname.c_str(), 31);
							strncpy((char*)zc + 32, dest.c_str(), 15);
							z->Send(kZoneChange, zc, sizeof(zc));
							asked = NowMs();
						}
					}
					else if (p->opcode == kZoneChange && p->size >= 48)	// the zone's answer: where we go, or nothing
					{
						std::string dest((const char*)b + 32, strnlen((const char*)b + 32, 16));
						if (dest.empty())
						{
							BotLog(charname, st.zone, "action=ZoneChange result=refused");
							teleported = false;
						}
						else
							confirmed = dest;
					}
				}
				DeleteAll(got);
				long now = NowMs();
				float step = 20.0f * (now - last) / 1000.0f;
				last = now;
				if (!teleported)
				{
					float dx = gx - meX, dy = gy - meY, d = sqrtf(dx * dx + dy * dy);
					bool walking = d > 1;
					if (walking)
					{
						float s2 = std::min(step, d);
						meX += dx / d * s2;
						meY += dy / d * s2;
						if (line->mode == 0) meZ = line->z;
					}
					else
					{
						// on the spot and nothing came: step around inside the range
						float a = (rand() % 360) * 3.14159f / 180;
						gx = (line->mode == 0 ? line->x : gx) + cosf(a) * std::max(1.0f, line->range * 0.5f);
						gy = (line->mode == 0 ? line->y : gy) + sinf(a) * std::max(1.0f, line->range * 0.5f);
					}
					unsigned char u[15];
					memset(u, 0, sizeof(u));
					short y = (short)meY, x = (short)meX, zz = (short)(meZ * 10);
					u[0] = myId & 0xff; u[1] = myId >> 8;
					u[2] = walking ? 22 : 0;
					u[3] = HeadingTowards(meX, meY, gx, gy);
					memcpy(u + 5, &y, 2); memcpy(u + 7, &x, 2); memcpy(u + 9, &zz, 2);
					u[11] = walking ? ((++tick & 1) ? 1 : 2) : 0;
					z->Send(kClientUpdate, u, sizeof(u));
				}
				else if (now - asked > 15000)
				{
					BotLog(charname, st.zone, "action=ZoneChange result=no_answer");
					teleported = false;
				}
			}
			if (killed)
				break;	// the zone sends the dead to their bind point: not a leg done
			if (confirmed.empty())
			{
				BotLog(charname, st.zone, "action=Travel result=timeout to=%s pos=%.0f,%.0f", to.c_str(), meX, meY);
				break;
			}
			// back to World with the ticket, into the new zone
			std::string from = st.zone;
			long t0 = NowMs();
			z->Disconnect();
			delete z;
			z = new EqSession();
			st = ZoneState();
			reuseWorldTicket = true;
			bool in = EnterZone(host, user, pass, charname, *z, st);
			reuseWorldTicket = false;
			if (!in || st.zone != confirmed)
			{
				BotLog(charname, "-", "action=Zoned result=failed from=%s to=%s landed=%s", from.c_str(), confirmed.c_str(), st.zone.c_str());
				break;
			}
			std::vector<unsigned char> npp = DecodeProfile(st.profile);
			float nx = 0, ny = 0;
			if (npp.size() >= 2420) { memcpy(&ny, &npp[2408], 4); memcpy(&nx, &npp[2412], 4); }
			BotLog(charname, st.zone, "action=Zoned from=%s to=%s ms=%ld pos=%.0f,%.0f", from.c_str(), st.zone.c_str(), NowMs() - t0, nx, ny);
			ok++;
		}
		return ok;
	}

	// ---- milestone 5: a trip to town ----
	// From where the bot stands: the zone line to `town`, the merchants there (closest first, six at most):
	// the first that opens buys what the packs hold (everything but food and drink); each is asked for a
	// stack of the cheapest food and drink it sells until we have both; then back to the zone we came from.
	// One line per step: TownSell, TownBuy, Town, TownBack. Returns true when the bot is back in its hunting zone.
	// The road is walked without fighting back (the bot leaves healthy); killed on it, the trip is over.
	bool TownTrip(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname,
	              const std::string& town, const std::string& linesFile)
	{
		std::vector<ZoneLine> lines = LoadZoneLines(linesFile);
		EqSession* z = new EqSession();
		ZoneState st;
		bool in = false;
		for (int attempt = 0; attempt < 8 && !in; attempt++)
		{
			if (attempt)	// World refuses a login for about a minute after the last one ended
			{
				long until = NowMs() + 15000;
				while (NowMs() < until) { struct timeval tv = { 0, 200000 }; select(0, 0, 0, 0, &tv); }
				delete z;
				z = new EqSession();
				st = ZoneState();
			}
			in = EnterZone(host, user, pass, charname, *z, st);
		}
		if (!in) { BotLog(charname, "-", "action=Town result=login_failed"); delete z; return false; }
		std::string home = st.zone;
		std::vector<std::string> there(1, town), back(1, home);
		if (lines.empty() || TravelLegs(host, user, pass, charname, z, st, there, lines) != 1)
		{
			BotLog(charname, st.zone, "action=Town result=not_reached town=%s", town.c_str());
			z->Disconnect();
			delete z;
			return false;
		}
		std::vector<unsigned char> pp = DecodeProfile(st.profile);
		float meX = 0, meY = 0, meZ = 0;
		if (pp.size() >= 2420) { memcpy(&meY, &pp[2408], 4); memcpy(&meX, &pp[2412], 4); memcpy(&meZ, &pp[2416], 4); }
		std::vector<Packet*> early = z->Poll(1500);
		for (size_t i = 0; i < early.size(); i++)
			if (early[i]->opcode == (int16)0xf520 && early[i]->size >= 12 && (early[i]->pBuffer[4] | (early[i]->pBuffer[5] << 8)) == 16 && st.myId == 0)
				st.myId = early[i]->pBuffer[8] | (early[i]->pBuffer[9] << 8);
		DeleteAll(early);
		// what to sell: the packs (22-29) and what is in bags (250-329), but food (14) and drink (15)
		std::vector<int> toSell;
		bool haveFood = false, haveDrink = false;
		std::map<int, std::vector<unsigned char> > items = InventoryItems(st.inventory);
		for (std::map<int, std::vector<unsigned char> >::iterator i = items.begin(); i != items.end(); ++i)
		{
			if (i->second.size() < 220)
				continue;
			int type = i->second[194];
			if (type == 14) haveFood = true;
			else if (type == 15) haveDrink = true;
			else if ((i->first >= 22 && i->first <= 29) || (i->first >= 250 && i->first < 330)) toSell.push_back(i->first);
		}
		std::vector<SpawnInfo> merchants;
		std::vector<SpawnInfo> spawns = DecodeSpawns(st.spawnPackets);
		for (size_t i = 0; i < spawns.size(); i++)
			if (spawns[i].npc == 1 && (spawns[i].cls == 41 || spawns[i].cls == 32))	// Sony's 41; our MERCHANT is 32
				merchants.push_back(spawns[i]);
		const char* prefer = getenv("EQBOT_TOWN_NPC");	// a merchant known to sell food: tried first
		std::sort(merchants.begin(), merchants.end(), [&](const SpawnInfo& a, const SpawnInfo& b) {
			bool pa = prefer && a.name.compare(0, strlen(prefer), prefer) == 0, pb = prefer && b.name.compare(0, strlen(prefer), prefer) == 0;
			if (pa != pb) return pa;
			return (a.x - meX) * (a.x - meX) + (a.y - meY) * (a.y - meY) < (b.x - meX) * (b.x - meX) + (b.y - meY) * (b.y - meY); });
		unsigned tick = 0;
		auto send = [&](bool walking, float tx, float ty) {
			unsigned char u[15];
			memset(u, 0, sizeof(u));
			short y = (short)meY, x = (short)meX, zz = (short)(meZ * 10);
			u[0] = st.myId & 0xff; u[1] = st.myId >> 8;
			u[2] = walking ? 22 : 0;
			u[3] = HeadingTowards(meX, meY, tx, ty);
			memcpy(u + 5, &y, 2); memcpy(u + 7, &x, 2); memcpy(u + 9, &zz, 2);
			u[11] = walking ? ((++tick & 1) ? 1 : 2) : 0;
			z->Send(kClientUpdate, u, sizeof(u));
		};
		int sold = 0, visited = 0;
		bool didSell = toSell.empty();
		for (size_t m = 0; m < merchants.size() && visited < 6 && !(didSell && haveFood && haveDrink); m++)
		{
			const SpawnInfo& mer = merchants[m];
			visited++;
			long last = NowMs(), walkStart = last;
			while (NowMs() - walkStart < 240000)	// straight to it, 20 units a second, stop 4 away
			{
				{ std::vector<Packet*> g = z->Poll(250); DeleteAll(g); }
				long now = NowMs();
				float dx = mer.x - meX, dy = mer.y - meY, d = sqrtf(dx * dx + dy * dy), step = 20.0f * (now - last) / 1000.0f;
				last = now;
				if (d <= 4) break;
				float s = std::min(step, d - 3);
				meX += dx / d * s; meY += dy / d * s; meZ = mer.z;
				send(true, mer.x, mer.y);
			}
			send(false, mer.x, mer.y);
			uint16_t aim[2] = { (uint16_t)mer.id, 0 };
			z->Send(kClientTarget, aim, sizeof(aim));
			unsigned char click[16];	// OP_ShopRequest: Merchant_Click_Struct (entity id, player id, 4 bytes, price multiplier)
			memset(click, 0, sizeof(click));
			click[0] = mer.id & 0xff; click[1] = mer.id >> 8;
			z->Send(0x0b20, click, sizeof(click));
			std::vector<Packet*> shop = Collect(*z, 3000, [](const std::vector<Packet*>&) { return false; });
			bool opened = false;
			int foodSlot = -1, drinkSlot = -1, foodCost = 0x7fffffff, drinkCost = 0x7fffffff, foodItem = 0, drinkItem = 0;
			for (size_t i = 0; i < shop.size(); i++)
			{
				const Packet* p = shop[i];
				if (p->opcode == 0x0b20 && p->size >= 9 && p->pBuffer[8] == 1)
					opened = true;
				if (p->opcode != 0x0c20 || p->size < 5 + 220)
					continue;
				const unsigned char* it = p->pBuffer + 5;	// OP_ShopItem: the Item_Struct after 5 bytes
				int cost = it[140] | (it[141] << 8) | (it[142] << 16) | (it[143] << 24), type = it[194];
				int slot = (short)(it[134] | (it[135] << 8)), nr = it[130] | (it[131] << 8);
				if (type == 14 && cost > 0 && cost < foodCost) { foodCost = cost; foodSlot = slot; foodItem = nr; }
				if (type == 15 && cost > 0 && cost < drinkCost) { drinkCost = cost; drinkSlot = slot; drinkItem = nr; }
			}
			DeleteAll(shop);
			if (!opened)
			{
				BotLog(charname, st.zone, "action=TownShop merchant=%s result=closed", mer.name.c_str());
				continue;
			}
			auto trade = [&](int16 opcode, int slot, int qty, int& gotQty, int& gotCost) {
				unsigned char b[20];	// Merchant_Purchase_Struct: npc, player, slot at 8, quantity at 12, cost at 16
				memset(b, 0, sizeof(b));
				b[0] = mer.id & 0xff; b[1] = mer.id >> 8;
				b[4] = st.myId & 0xff; b[5] = st.myId >> 8;
				b[8] = slot & 0xff; b[9] = (slot >> 8) & 0xff;
				b[12] = (unsigned char)qty;
				z->Send(opcode, b, sizeof(b));
				std::vector<Packet*> r = Collect(*z, 2500, [&](const std::vector<Packet*>& g) {
					for (size_t i = 0; i < g.size(); i++) if (g[i]->opcode == opcode) return true;
					return HasText(g, "afford"); });
				gotQty = gotCost = -1;
				for (size_t i = 0; i < r.size(); i++)
					if (r[i]->opcode == opcode && r[i]->size >= 20)
					{
						gotQty = r[i]->pBuffer[12];
						gotCost = r[i]->pBuffer[16] | (r[i]->pBuffer[17] << 8) | (r[i]->pBuffer[18] << 16) | (r[i]->pBuffer[19] << 24);
					}
				DeleteAll(r);
			};
			if (!didSell)
			{
				for (size_t i = 0; i < toSell.size(); i++)
				{
					int q, cost;
					trade(0x2820, toSell[i], 20, q, cost);	// OP_ShopPlayerSell: the zone caps the quantity at what the slot holds
					int nr = items[toSell[i]][130] | (items[toSell[i]][131] << 8);
					BotLog(charname, st.zone, "action=TownSell merchant=%s slot=%d item=%d qty=%d", mer.name.c_str(), toSell[i], nr, q);	// (the zone echoes the request: no price in it)
					if (q >= 0) sold++;
				}
				didSell = true;
			}
			// A little of each first (5, or what the purse allows: the zone says "You cannot afford that"),
			// then up to a stack of 20 with what is left
			auto buy = [&](int slot, int item, const char* what, const int* tries, int n) {
				for (int k = 0; k < n; k++)
				{
					int q, cost;
					trade(0x2720, slot, tries[k], q, cost);	// OP_ShopPlayerBuy
					if (q > 0)
					{
						BotLog(charname, st.zone, "action=TownBuy merchant=%s what=%s item=%d qty=%d copper=%d", mer.name.c_str(), what, item, q, cost);
						return true;
					}
				}
				return false;
			};
			static const int first[] = { 5, 2, 1 }, more[] = { 15, 10, 5 };
			bool gotDrink = !haveDrink && drinkSlot >= 0 && buy(drinkSlot, drinkItem, "drink", first, 3);
			bool gotFood = !haveFood && foodSlot >= 0 && buy(foodSlot, foodItem, "food", first, 3);
			if (gotFood) buy(foodSlot, foodItem, "food", more, 3);
			if (gotDrink) buy(drinkSlot, drinkItem, "drink", more, 3);
			if (!haveDrink && drinkSlot >= 0 && !gotDrink) BotLog(charname, st.zone, "action=TownBuy merchant=%s what=drink item=%d qty=0 why=cannot_afford", mer.name.c_str(), drinkItem);
			if (!haveFood && foodSlot >= 0 && !gotFood) BotLog(charname, st.zone, "action=TownBuy merchant=%s what=food item=%d qty=0 why=cannot_afford", mer.name.c_str(), foodItem);
			haveDrink = haveDrink || gotDrink;
			haveFood = haveFood || gotFood;
			z->Send(0x3720, 0, 0);	// OP_ShopEnd: close the window
			{ std::vector<Packet*> g = Collect(*z, 600, [](const std::vector<Packet*>&) { return false; }); DeleteAll(g); }
		}
		BotLog(charname, st.zone, "action=Town town=%s merchants=%d sold=%d food=%d drink=%d", town.c_str(), visited, sold, haveFood ? 1 : 0, haveDrink ? 1 : 0);
		// back: a new session, so that it starts from where we stand (the legs walk from the profile's position)
		z->Disconnect();
		delete z;
		z = new EqSession();
		st = ZoneState();
		in = false;
		for (int attempt = 0; attempt < 8 && !in; attempt++)
		{
			long until = NowMs() + 15000;
			while (NowMs() < until) { struct timeval tv = { 0, 200000 }; select(0, 0, 0, 0, &tv); }
			if (attempt) { delete z; z = new EqSession(); st = ZoneState(); }
			in = EnterZone(host, user, pass, charname, *z, st);
		}
		bool ok = in && TravelLegs(host, user, pass, charname, z, st, back, lines) == 1;
		BotLog(charname, st.zone, "action=TownBack result=%s zone=%s", ok ? "ok" : "failed", st.zone.c_str());
		if (in) z->Disconnect();
		delete z;
		return ok;
	}

	void Usage()
	{
		fprintf(stderr, "usage: eqbot login  <login-host> <user> <password> [port=5999]\n"
		                "       eqbot create <login-host> <user> <password> <character>\n"
		                "       eqbot play   <login-host> <user> <password> <character>\n"
		                "       eqbot test   <login-host> <user> <password> <character>\n"
		                "       eqbot walk   <login-host> <user> <password> <character> <waypoints file> [seconds=60]\n"
		                "       eqbot hunt   <login-host> <user> <password> <character> [seconds=300]\n"
		                "       eqbot cast   <login-host> <user> <password> <character> <gem> <spell id>\n"
		                "       eqbot tell   <login-host> <user> <password> <character> <to> <text> [seconds=10]\n"
		                "       eqbot travel <login-host> <user> <password> <character> <zone,zone,...> [zonelines.tsv]\n");
	}
}

int main(int argc, char** argv)
{
	if (argc >= 5 && std::string(argv[1]) == "login")
		return Login(argv[2], argc > 5 ? atoi(argv[5]) : 5999, argv[3], argv[4]);
	if (argc >= 6 && std::string(argv[1]) == "create")
		return Create(argv[2], argv[3], argv[4], argv[5]);
	if (argc >= 6 && std::string(argv[1]) == "play")
		return Play(argv[2], argv[3], argv[4], argv[5]);
	if (argc >= 6 && std::string(argv[1]) == "test")
		return Test(argv[2], argv[3], argv[4], argv[5]);
	if (argc >= 7 && std::string(argv[1]) == "travel")
		return Travel(argv[2], argv[3], argv[4], argv[5], argv[6], argc > 7 ? argv[7] : "../botd/paths/zonelines.tsv");
	if (argc >= 8 && std::string(argv[1]) == "tell")
		return Tell(argv[2], argv[3], argv[4], argv[5], argv[6], argv[7], argc > 8 ? atoi(argv[8]) : 10);
	if (argc >= 8 && std::string(argv[1]) == "cast")
		return Cast(argv[2], argv[3], argv[4], argv[5], atoi(argv[6]), atoi(argv[7]));
	if (argc >= 6 && std::string(argv[1]) == "hunt")
		return Hunt(argv[2], argv[3], argv[4], argv[5], argc > 6 ? atoi(argv[6]) : 300);
	if (argc >= 7 && std::string(argv[1]) == "walk")
		return Walk(argv[2], argv[3], argv[4], argv[5], argv[6], argc > 7 ? atoi(argv[7]) : 60);
	Usage();
	return 2;
}
