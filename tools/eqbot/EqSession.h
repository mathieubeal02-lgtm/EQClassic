#ifndef EQBOT_EQSESSION_H
#define EQBOT_EQSESSION_H

// A client-side EQ UDP session for the test bot. It reuses the server's own protocol layer
// (EQPacketManager: sequencing, acks, resends, fragments): the wire format is symmetric, so the
// bot speaks exactly what the servers speak.

#include <string>
#include <vector>

#include "config.h"
#include "types.h"
#include "EQPacketManager.h"

namespace EqBot
{
	typedef EQC::Common::Network::APPLAYER Packet;

	class EqSession
	{
	public:
		EqSession();
		~EqSession();

		bool Open(const std::string& host, int port);
		void Close();
		// Send the protocol's closing bits so the peer drops the session at once instead of
		// waiting for resend timeouts (what a client going link-dead would cause).
		void Disconnect();

		// Queue an application packet (opcode + payload) and push it on the wire.
		void Send(int16 opcode, const void* data, size_t size);
		// Pump the network for up to `ms` milliseconds; returns every application packet received
		// (the caller owns them). Returns early as soon as at least one packet arrived.
		std::vector<Packet*> Poll(int ms);
		// Wait up to `ms` for a packet with this opcode. Other packets are appended to `others`
		// (or deleted when others is null). Returns 0 on timeout.
		Packet* WaitFor(int16 opcode, int ms, std::vector<Packet*>* others = 0);

		const std::string& Peer() const { return peer_name; }
		// Our packets the peer has not acked yet, and whether our side of the protocol still runs
		// (it gives up, like the servers, after 15 resends of one packet).
		int Pending() const { return manager ? manager->PacketsPending() : 0; }
		bool Active() const { return manager && manager->CheckActive(); }

		// The last datagrams in and out (time, direction, first bytes: the protocol header), printed
		// when the session ends without us closing it, to see whose acks went missing.
		void DumpRecent() const;

	private:
		void Flush();
		void Remember(bool in, const unsigned char* data, size_t size);

		int sock;
		std::string peer_name;
		unsigned char peer_addr[16];	// sockaddr_in, kept opaque here
		EQC::Common::Network::EQPacketManager* manager;
		struct Seen { long ms; bool in; unsigned short size; unsigned char head[24]; };
		std::vector<Seen> recent;
		size_t recentNext;
	};

	// Helpers
	std::string HexDump(const unsigned char* data, size_t size, size_t max = 64);
	void DeleteAll(std::vector<Packet*>& packets);
}

#endif
