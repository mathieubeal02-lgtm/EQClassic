// Two EQPacketManagers wired back to back (no sockets): a server side streams more than 65536
// packets to a client side that acks them, so the 16-bit sequence number wraps. Before the fix the
// wrap started the sequence over (new random ack numbers) while packets of the old numbering were
// still waiting for their ack; none could ever come and the server dropped the client after 15
// resends. Built and run by CTest.
#include "EQPacketManager.h"

#include <cstdio>
#include <cstring>

using namespace EQC::Common::Network;

static int failures = 0;
#define EXPECT(cond) do { if (!(cond)) { printf("FAIL line %d: %s\n", __LINE__, #cond); failures++; } } while (0)

// Moves every datagram `from` has queued into `to`; returns how many carried the SEQStart flag.
static int Deliver(EQPacketManager& from, EQPacketManager& to)
{
	int starts = 0;
	MySendPacketStruct* p;
	while ((p = from.SendQueue.pop()))
	{
		if (p->buffer[0] & 0x20)
			starts++;
		to.ParceEQPacket((int16)p->size, p->buffer);
		delete[] p->buffer;
		delete p;
	}
	return starts;
}

int main()
{
	EQPacketManager server, client;
	const int total = 70000;
	int starts = 0, received = 0, inOrder = 1, maxPending = 0;
	for (int i = 0; i < total; i++)
	{
		APPLAYER app(0x4120, 4);
		memcpy(app.pBuffer, &i, 4);
		server.MakeEQPacket(&app);
		starts += Deliver(server, client);
		APPLAYER* got;
		while ((got = client.OutQueue.pop()))
		{
			int n;
			memcpy(&n, got->pBuffer, 4);
			inOrder &= n == received;
			received++;
			delete got;
		}
		// the client answers now and then; its packets carry the ack of what it got
		if (i % 10 == 9)
		{
			APPLAYER reply(0x4220, 2);
			memset(reply.pBuffer, 0, 2);
			client.MakeEQPacket(&reply);
			Deliver(client, server);
		}
		if (server.PacketsPending() > maxPending)
			maxPending = server.PacketsPending();
	}
	printf("%d packets sent, %d received, %d SEQStart, at most %d waiting for an ack\n", total, received, starts, maxPending);
	EXPECT(received == total);
	EXPECT(inOrder);
	EXPECT(starts == 1);			// only the first packet starts the sequence
	EXPECT(maxPending < 32);		// acks keep matching across the wrap
	EXPECT(server.CheckActive());
	printf(failures ? "[FAIL] packet manager\n" : "[ OK ] packet manager\n");
	return failures ? 1 : 0;
}
