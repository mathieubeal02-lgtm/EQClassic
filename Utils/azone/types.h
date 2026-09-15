#ifndef __EQCLIENT_TYPES_H_
#define __EQCLIENT_TYPES_H_

typedef unsigned long uint32;
typedef long int32;

typedef unsigned short uint16;
typedef short int16;

typedef unsigned char uint8;
typedef unsigned char uchar;
typedef char int8;

#define null 0

// Same helpers as the old LS/common/types.h that azone was originally built against.
#define safe_delete(d) if(d) { delete d; d=0; }
#define safe_delete_array(d) if(d) { delete[] d; d=0; }

#endif
