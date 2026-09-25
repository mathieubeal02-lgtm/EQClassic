// Unit tests for Common/Include/SQLEscape.h. Built and run by CTest (EQC_BUILD_TESTS).
#include "SQLEscape.h"

#include <cstdio>
#include <string>

static int failures = 0;

static void expect(const std::string& got, const std::string& want, const char* what)
{
	if (got != want)
	{
		printf("FAIL %s: got [%s] want [%s]\n", what, got.c_str(), want.c_str());
		failures++;
	}
}

int main()
{
	expect(SQLEscape("Aamaden"), "Aamaden", "plain name unchanged");
	expect(SQLEscape(""), "", "empty string");
	expect(SQLEscape((const char*)0), "", "null pointer");
	expect(SQLEscape("O'Brien"), "O\\'Brien", "single quote");
	expect(SQLEscape("say \"hi\""), "say \\\"hi\\\"", "double quote");
	expect(SQLEscape("a\\b"), "a\\\\b", "backslash");
	expect(SQLEscape("l1\nl2\r"), "l1\\nl2\\r", "newlines");
	expect(SQLEscape("x\032y"), "x\\Zy", "ctrl-z");
	expect(SQLEscape(std::string("a\0b", 3)), "a\\0b", "embedded NUL (std::string)");
	expect(SQLEscape("a\0b", 3), "a\\0b", "embedded NUL (buffer+len)");
	expect(SQLEscape("caf\xe9"), "caf\xe9", "latin1 byte passes through");

	// Classic injection payload: the quote must not terminate the literal.
	const std::string q = "SELECT id FROM account WHERE name='" + SQLEscape("x' OR '1'='1") + "'";
	expect(q, "SELECT id FROM account WHERE name='x\\' OR \\'1\\'=\\'1'", "injection neutralised");
	// Backslash before a quote must not un-escape it.
	expect(SQLEscape("\\'"), "\\\\\\'", "backslash-quote");

	if (failures == 0)
		printf("sql_escape_test: all tests passed\n");
	return failures == 0 ? 0 : 1;
}
