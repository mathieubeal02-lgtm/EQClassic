#include "EqSession.h"
#include "timer.h"

#include <arpa/inet.h>
#include <netdb.h>
#include <sys/select.h>
#include <sys/socket.h>
#include <sys/time.h>
#include <unistd.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace EqBot
{
	using EQC::Common::Network::EQPacketManager;

	static long NowMs()
	{
		timeval tv;
		gettimeofday(&tv, 0);
		return tv.tv_sec * 1000L + tv.tv_usec / 1000;
	}

	EqSession::EqSession() : sock(-1), manager(0)
	{
		memset(peer_addr, 0, sizeof(peer_addr));
	}

	EqSession::~EqSession()
	{
		Close();
	}

	bool EqSession::Open(const std::string& host, int port)
	{
		Close();
		addrinfo hints, *res = 0;
		memset(&hints, 0, sizeof(hints));
		hints.ai_family = AF_INET;
		hints.ai_socktype = SOCK_DGRAM;
		char portstr[16];
		snprintf(portstr, sizeof(portstr), "%d", port);
		if (getaddrinfo(host.c_str(), portstr, &hints, &res) != 0 || !res)
			return false;
		memcpy(peer_addr, res->ai_addr, sizeof(sockaddr_in));
		freeaddrinfo(res);

		sock = socket(AF_INET, SOCK_DGRAM, 0);
		if (sock < 0)
			return false;
		peer_name = host + ":" + portstr;
		manager = new EQPacketManager();
		return true;
	}

	void EqSession::Close()
	{
		if (sock >= 0)
			close(sock);
		sock = -1;
		delete manager;
		manager = 0;
	}

	void EqSession::Disconnect()
	{
		if (!manager)
			return;
		manager->Close();
		manager->MakeEQPacket(0);
		Flush();
	}

	void EqSession::Flush()
	{
		MySendPacketStruct* p;
		while ((p = manager->SendQueue.pop()))
		{
			sendto(sock, p->buffer, p->size, 0, (sockaddr*)peer_addr, sizeof(sockaddr_in));
			delete[] p->buffer;
			delete p;
		}
	}

	void EqSession::Send(int16 opcode, const void* data, size_t size)
	{
		Packet app(opcode, (int32)size);
		if (size && data)
			memcpy(app.pBuffer, data, size);
		manager->MakeEQPacket(&app);
		Flush();
	}

	std::vector<Packet*> EqSession::Poll(int ms)
	{
		std::vector<Packet*> out;
		long deadline = NowMs() + ms;
		unsigned char buf[4096];
		while (true)
		{
			// The protocol timers (acks, resends, keep-alive) read a cached clock that the servers
			// refresh once per loop; without this the bot never acks and the peer resends everything.
			Timer::SetCurrentTime();
			manager->CheckTimers();
			Flush();

			Packet* app;
			while ((app = manager->OutQueue.pop()))
				out.push_back(app);
			if (!out.empty())
				return out;

			long left = deadline - NowMs();
			if (left <= 0)
				return out;

			fd_set fds;
			FD_ZERO(&fds);
			FD_SET(sock, &fds);
			timeval tv;
			long wait = left < 50 ? left : 50;
			tv.tv_sec = 0;
			tv.tv_usec = wait * 1000;
			if (select(sock + 1, &fds, 0, 0, &tv) > 0)
			{
				ssize_t n = recv(sock, buf, sizeof(buf), 0);
				if (n > 0 && getenv("EQBOT_RAW"))
					printf("       raw <- %d bytes: %s\n", (int)n, HexDump(buf, (size_t)n, 24).c_str());
				// 8-byte datagrams are the login server's keep-alives (flags, sequence, CRC): nothing
				// to ack or deliver, and EQPacket rejects anything under 10 bytes loudly.
				if (n >= 10)
					manager->ParceEQPacket((int16)n, buf);
			}
		}
	}

	Packet* EqSession::WaitFor(int16 opcode, int ms, std::vector<Packet*>* others)
	{
		long deadline = NowMs() + ms;
		while (NowMs() < deadline)
		{
			std::vector<Packet*> got = Poll((int)(deadline - NowMs()));
			Packet* found = 0;
			for (size_t i = 0; i < got.size(); i++)
			{
				if (!found && got[i]->opcode == opcode)
					found = got[i];
				else if (others)
					others->push_back(got[i]);
				else
					delete got[i];
			}
			if (found)
				return found;
		}
		return 0;
	}

	std::string HexDump(const unsigned char* data, size_t size, size_t max)
	{
		std::string s;
		char b[4];
		for (size_t i = 0; i < size && i < max; i++)
		{
			snprintf(b, sizeof(b), "%02x ", data[i]);
			s += b;
		}
		if (size > max)
			s += "...";
		return s;
	}

	void DeleteAll(std::vector<Packet*>& packets)
	{
		for (size_t i = 0; i < packets.size(); i++)
			delete packets[i];
		packets.clear();
	}
}
