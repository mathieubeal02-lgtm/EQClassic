// eqbot: headless EverQuest Trilogy test client for the EQClassic servers.
//
//   eqbot login  <login-host> <user> <password> [port]
//   eqbot create <login-host> <user> <password> <character>
//   eqbot play   <login-host> <user> <password> <character>
//
// login:  the login server handshake the real client does, checking every answer:
//         LoginInfo (DES-encrypted credentials) -> SessionId, LoginBanner, ServerList,
//         RequestServerStatus -> SendServerStatus, SessionKey -> session key, AllFinish.
// create: login, world, and character creation from a packet captured from the real client
//         (no-op when the character exists).
// play:   login, world, enter world, then the zone handshake up to "Enterzone complete", and a
//         clean disconnect.
// Exit code 0 when every step succeeded. Output is one line per step, for CI logs.
// EQBOT_VERBOSE=1 lists the zone packets, EQBOT_RAW=1 dumps every datagram received.
// EQBOT_STAY=<seconds> keeps `play` in the zone and checks the height of moving mobs.
#include <openssl/des.h>
#include <stdint.h>
#include <zlib.h>
#include <sys/time.h>

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <map>
#include <string>
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
	bool EnterWorldServer(EqSession& w, const std::string& host, const std::string& user, const std::string& pass,
	                      std::vector<std::string>* names)
	{
		std::string account, key, world;
		if (Login(host, 5999, user, pass, &account, &key, &world) != 0)
			return false;
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
		approval[32] = 9;
		approval[36] = 10;
		w.Send(kNameApproval, approval, sizeof(approval));
		Packet* p = w.WaitFor(kNameApproval, TIMEOUT_MS);
		bool approved = p && p->size >= 1 && p->pBuffer[0] == 1;
		Step(approved, "name approval", p ? (approved ? charname : "name refused") : "no answer");
		delete p;
		if (!approved)
			return 1;

		std::vector<unsigned char> cc(kCharCreateTemplate, kCharCreateTemplate + sizeof(kCharCreateTemplate));
		strncpy((char*)cc.data(), charname.c_str(), 29);
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
	};

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
		DeleteAll(got);

		z.Send(kZoneRequest3, name30, sizeof(name30));
		{
			std::vector<Packet*> early = z.Poll(1000);
			for (size_t i = 0; i < early.size(); i++)
			{
				if (getenv("EQBOT_VERBOSE"))
					printf("       step3 -> 0x%04x %d bytes\n", (unsigned)(unsigned short)early[i]->opcode, (int)early[i]->size);
				if (early[i]->opcode == kZoneSpawns)
					st.spawnPackets.push_back(std::vector<unsigned char>(early[i]->pBuffer, early[i]->pBuffer + early[i]->size));
			}
			DeleteAll(early);
		}
		z.Send(kZoneRequest4, 0, 0);
		p = z.WaitFor(kZoneDone, 20000, &others);
		int spawns = (int)others.size();
		for (size_t i = 0; i < others.size(); i++)
			if (others[i]->opcode == kZoneSpawns)
				st.spawnPackets.push_back(std::vector<unsigned char>(others[i]->pBuffer, others[i]->pBuffer + others[i]->size));
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
				if (late[i]->opcode == kZoneSpawns)
					st.spawnPackets.push_back(std::vector<unsigned char>(late[i]->pBuffer, late[i]->pBuffer + late[i]->size));
			}
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

	struct SpawnInfo { int id; std::string name; int npc; int level; float x, y, z; };

	// NewSpawn_Struct[] of the zone's spawn packets (EncryptZoneSpawnPacket + DeflatePacket):
	// 168 bytes each, the Spawn_Struct after a 4-byte placeholder.
	SpawnInfo ParseSpawn(const unsigned char* sp)
	{
		SpawnInfo si;
		si.id = sp[62] | (sp[63] << 8);
		si.name = std::string((const char*)sp + 100, strnlen((const char*)sp + 100, 30));
		si.npc = sp[73];
		si.level = sp[76];
		si.y = (short)(sp[51] | (sp[52] << 8));
		si.x = (short)(sp[53] | (sp[54] << 8));
		si.z = (short)(sp[55] | (sp[56] << 8));
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
		std::vector<Packet*> got = Collect(z, 10000, [](const std::vector<Packet*>& g) { return HasText(g, "Location"); });
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
			const SpawnInfo* prey = 0;
			for (size_t i = 0; i < spawns.size(); i++)
				if (spawns[i].npc == 1 && spawns[i].level <= 3 && spawns[i].name.compare(0, 2, "a_") == 0)
				{
					prey = &spawns[i];
					break;
				}
			Say(z, charname, "#clearcursor");
			DeleteAll(got = Collect(z, 1500, [](const std::vector<Packet*>&) { return false; }));
			Say(z, charname, "#si " + std::to_string(weapon));
			got = Collect(z, 10000, [](const std::vector<Packet*>& g) { return SummonedItemId(g) != 0; });
			bool summoned = SummonedItemId(got) == weapon;
			DeleteAll(got);
			if (!prey || !summoned)
				Step(false, "melee with a weapon", !prey ? "no small NPC (a_..., level 3 or less) in the zone" : "the weapon was not summoned");
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
				unsigned char u[15];
				memset(u, 0, sizeof(u));
				short y = (short)py, x = (short)(px + 2), zz = (short)(pz + 4);	// eye height rather than the feet: line of sight
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
						short ny = (short)py, nx = (short)(px + 2), nz = (short)(pz + 4);
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
			short y = (short)npc->y, x = (short)(npc->x + 3), zz = (short)npc->z;
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

	void Usage()
	{
		fprintf(stderr, "usage: eqbot login  <login-host> <user> <password> [port=5999]\n"
		                "       eqbot create <login-host> <user> <password> <character>\n"
		                "       eqbot play   <login-host> <user> <password> <character>\n"
		                "       eqbot test   <login-host> <user> <password> <character>\n");
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
	Usage();
	return 2;
}
