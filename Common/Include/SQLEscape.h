#ifndef EQC_SQLESCAPE_H
#define EQC_SQLESCAPE_H

// Escaping for values interpolated into SQL string literals ('...' or "...") with
// MakeAnyLenString. Header-only so World, Zone, SharedMemory and LS/Login can share it.
//
// Escapes the same characters as mysql_real_escape_string does for single-byte character
// sets (the eqclassic schema is latin1): NUL, \n, \r, \, ', " and Ctrl-Z. It does not need a
// live MYSQL handle. It only protects values that sit inside quotes; never use it for
// identifiers, numbers or pre-built SQL fragments.
//
// Usage: MakeAnyLenString(&query, "... WHERE name='%s'", SQLEscape(name).c_str())
// The temporary lives until the end of the full expression, which covers the call.

#include <string>
#include <cstring>

inline std::string SQLEscape(const char* from, size_t len)
{
	std::string out;
	if (from == 0)
		return out;
	out.reserve(len + len / 8 + 2);
	for (size_t i = 0; i < len; i++)
	{
		const char c = from[i];
		switch (c)
		{
			case '\0':   out += "\\0";  break;
			case '\n':   out += "\\n";  break;
			case '\r':   out += "\\r";  break;
			case '\\':   out += "\\\\"; break;
			case '\'':   out += "\\'";  break;
			case '"':    out += "\\\""; break;
			case '\032': out += "\\Z";  break;
			default:     out += c;      break;
		}
	}
	return out;
}

inline std::string SQLEscape(const char* from)
{
	return from ? SQLEscape(from, strlen(from)) : std::string();
}

inline std::string SQLEscape(const std::string& from)
{
	return SQLEscape(from.data(), from.size());
}

#endif
