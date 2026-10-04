// ***************************************************************
//  EQCException   ·  date: 21/12/2007
//  -------------------------------------------------------------
//  Copyright (C) 2007 - All Rights Reserved
// ***************************************************************
// 
// ***************************************************************

#include <cstring>
#include <fstream>
#include "FragmentGroup.h"

namespace EQC
{
	namespace Common
	{
		namespace Network
		{
			FragmentGroup::FragmentGroup(int16 seq, int16 opcode, int16 num_fragments)
			{
				this->seq = seq;
				this->opcode = opcode;
				this->num_fragments = num_fragments;
				this->received = 0;
				fragment = new Fragment[num_fragments];
			}

			FragmentGroup::~FragmentGroup()
			{
				safe_delete_array(fragment);//delete[] fragment;
			}

			bool FragmentGroup::Add(int16 frag_id, uchar* data, int32 size)
			{
				//Yeahlight: The frag_id references a fragment within the group
				if(frag_id < num_fragments)
				{
					if (fragment[frag_id].GetData() != 0)
						return true; // resend of a fragment we already have
					fragment[frag_id].SetData(data, size);
					received++;
					return true;
				}
				//Yeahlight: The frag_id is attempting to reference an element outside the bounds of the group array
				else
				{
					ofstream myfile;
					myfile.open("fragmentDebug.txt", ios::app);
					myfile<<"A call to FragmentGroup::Add() has been made for which frag_id is outside the bounds of fragment[]"<<endl;
					myfile<<"  frag_id: "<<frag_id<<endl;
					myfile<<"  data:    "<<&data<<endl;
					myfile<<"  size:    "<<size<<endl;
					myfile.close();
					return false;
				}
			}

			uchar* FragmentGroup::AssembleData(int32* size)
			{
				uchar* buf;
				uchar* p;
				int i;

				*size = 0;	
				for(i=0; i<num_fragments; i++)
				{
					*size+=fragment[i].GetSize();		
				}
				// one zero byte past the end: a string the client did not terminate cannot be read past the packet
				buf = new uchar[*size + 1];
				buf[*size] = 0;
				p = buf;
				for(i=0; i<num_fragments; i++)
				{
					memcpy(p, fragment[i].GetData(), fragment[i].GetSize());
					p += fragment[i].GetSize();
				}

				return buf;
			}

		}
	}
}