// Unit tests for fragment reassembly bounds (Common/Include/FragmentLimits.h) and
// EQC::Common::Network::FragmentGroup. Built and run by CTest.
#include "FragmentLimits.h"
#include "FragmentGroup.h"

#include <cstdio>
#include <cstring>

using namespace EQC::Common::Network;

static int failures = 0;
#define EXPECT(cond) do { if (!(cond)) { printf("FAIL line %d: %s\n", __LINE__, #cond); failures++; } } while (0)

int main()
{
	// Header validation
	EXPECT(FragmentInfoValid(0, 1));
	EXPECT(FragmentInfoValid(16, 17));
	EXPECT(!FragmentInfoValid(0, 0));                                 // empty group
	EXPECT(!FragmentInfoValid(5, 5));                                 // index == count
	EXPECT(!FragmentInfoValid(0xFFFF, 0xFFFF));                       // max 16-bit values
	EXPECT(!FragmentInfoValid(0, MAX_FRAGMENTS_PER_PACKET + 1));      // oversized packet
	EXPECT(FragmentInfoValid(MAX_FRAGMENTS_PER_PACKET - 1, MAX_FRAGMENTS_PER_PACKET));

	// Group completes only when every fragment is there, in any order, resends ignored
	uchar a[] = "hello ", b[] = "fragmented ", c[] = "world";
	FragmentGroup g(7, 0x1234, 3);
	EXPECT(!g.IsComplete());
	EXPECT(g.Add(2, c, 5));
	EXPECT(!g.IsComplete());                  // last index first: must not complete (old behaviour did)
	EXPECT(g.Add(0, a, 6));
	EXPECT(g.Add(0, a, 6));                   // resend
	EXPECT(!g.IsComplete());
	EXPECT(!g.Add(3, a, 6));                  // out of range
	EXPECT(g.Add(1, b, 11));
	EXPECT(g.IsComplete());
	int32 size = 0;
	uchar* data = g.AssembleData(&size);
	EXPECT(size == 22);
	EXPECT(memcmp(data, "hello fragmented world", 22) == 0);
	delete[] data;

	if (failures == 0)
		printf("fragment_test: all tests passed\n");
	return failures == 0 ? 0 : 1;
}
