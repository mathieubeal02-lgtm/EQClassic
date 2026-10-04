// SeqDiff (Common/Include/SequenceMath.h): ordering of 16-bit sequence numbers across the wrap.
#include "../Common/Include/SequenceMath.h"
#include <cstdio>

static int failures = 0;
#define EXPECT_EQ(a, b) do { long _a = (a), _b = (b); if (_a != _b) { printf("FAIL line %d: %s = %ld, want %ld\n", __LINE__, #a, _a, _b); failures++; } } while (0)

int main()
{
	EXPECT_EQ(SeqDiff(10, 5), 5);
	EXPECT_EQ(SeqDiff(5, 10), -5);
	EXPECT_EQ(SeqDiff(2, 65534), 4);		// across the wrap: 2 comes 4 after 65534
	EXPECT_EQ(SeqDiff(65534, 2), -4);
	EXPECT_EQ(SeqDiff(0, 65535), 1);
	EXPECT_EQ(SeqDiff(7, 7), 0);
	if (failures == 0)
		printf("sequence_test: all tests passed\n");
	return failures == 0 ? 0 : 1;
}
