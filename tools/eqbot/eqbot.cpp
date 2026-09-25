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
#include <sys/time.h>

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

	// login -> world -> character select -> enter world -> zone handshake.
	int Play(const std::string& host, const std::string& user, const std::string& pass, const std::string& charname)
	{
		EqSession w;
		std::vector<std::string> names;
		if (!EnterWorldServer(w, host, user, pass, &names))
			return 1;
		bool found = false;
		for (size_t i = 0; i < names.size(); i++)
			found = found || names[i] == charname;
		Step(found, "character list", Join(names));
		if (!found)
			return 1;
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
			return 1;
		}
		std::string zip((const char*)p->pBuffer, strnlen((const char*)p->pBuffer, 75));
		std::string zname((const char*)p->pBuffer + 75, strnlen((const char*)p->pBuffer + 75, 53));
		int zport = (p->pBuffer[128] << 8) | p->pBuffer[129];
		char zb[128];
		snprintf(zb, sizeof(zb), "%s at %s:%d", zname.c_str(), zip.c_str(), zport);
		Step(true, "enter world", zb);
		delete p;

		// ---- zone ----
		EqSession z;
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
		delete p;
		DeleteAll(got);

		z.Send(kZoneRequest3, name30, sizeof(name30));
		z.Poll(1000);
		z.Send(kZoneRequest4, 0, 0);
		p = z.WaitFor(kZoneDone, 20000, &others);
		int spawns = (int)others.size();
		DeleteAll(others);
		snprintf(gb, sizeof(gb), "zone header + %d spawn/door/object packets", spawns);
		Step(p != 0, "zone data", p ? gb : "no answer");
		delete p;
		z.Send(kZoneDone, 0, 0);
		z.Poll(2000);
		Step(true, "in zone", charname + " in " + zname);
		if (getenv("EQBOT_STAY"))
			WatchMobs(z, atoi(getenv("EQBOT_STAY")));
		z.Disconnect();
		return failures ? 1 : 0;
	}

	void Usage()
	{
		fprintf(stderr, "usage: eqbot login  <login-host> <user> <password> [port=5999]\n"
		                "       eqbot create <login-host> <user> <password> <character>\n"
		                "       eqbot play   <login-host> <user> <password> <character>\n");
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
	Usage();
	return 2;
}
