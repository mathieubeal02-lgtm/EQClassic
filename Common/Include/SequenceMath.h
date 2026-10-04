#ifndef EQC_SEQUENCEMATH_H
#define EQC_SEQUENCEMATH_H

// Sequence and ack numbers are 16-bit and wrap from 65535 to 0. Comparing them by subtracting the
// unsigned values (as the packet manager did) breaks at the wrap: 2 - 65534 is -65532, so an ack for
// 2 no longer acknowledges 65534 and a new packet looks old. The difference taken modulo 2^16 as a
// signed number gives the right order for numbers less than 32768 apart (Harakiri's rev. 881: "an
// endless loop in the packet manager when people are connected for days").
inline short SeqDiff(unsigned short a, unsigned short b)
{
	return (short)(unsigned short)(a - b);
}

#endif
