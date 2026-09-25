#ifndef EQC_FRAGMENTLIMITS_H
#define EQC_FRAGMENTLIMITS_H

// Bounds for reassembling fragmented packets (old EQ UDP protocol). Shared by the zone/world
// packet manager (Common/Source/EQPacketManager.cpp) and the login server (LS/common/EQNetwork.cpp).
//
// The fragment header is fully client-controlled: seq, current index and total count (16 bits each).
// Without limits a client could make the server allocate a 65535-entry group per packet and keep
// thousands of never-completed groups alive (memory exhaustion), or reference out-of-range indexes.
// Idea taken from EQMacEmu's "Harden fragmented EQStream reassembly" (#436), reimplemented for
// this protocol: validate before allocating, bound the reassembled size, bound in-flight groups.

// Fragments carry 510/512 data bytes; the largest legitimate packet (player profile, ~8 KB) needs
// ~17 fragments. 1024 fragments = ~512 KB per reassembled packet.
static const unsigned int MAX_FRAGMENTS_PER_PACKET = 1024;

// A client normally has one fragmented packet in flight. Beyond this many incomplete groups on one
// connection, the pending groups are discarded.
static const unsigned int MAX_PENDING_FRAGMENT_GROUPS = 16;

// True if a fragment header (index `curr` of `total`) can be accepted.
inline bool FragmentInfoValid(unsigned int curr, unsigned int total)
{
	return total != 0 && total <= MAX_FRAGMENTS_PER_PACKET && curr < total;
}

#endif
