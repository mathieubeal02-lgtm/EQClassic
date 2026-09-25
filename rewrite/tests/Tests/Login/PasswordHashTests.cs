using EQClassic.Server.Accounts;

namespace EQClassic.Tests.Login;

public class PasswordHashTests
{
    [Fact]
    public void Matches_mysql_sha()
    {
        // SELECT SHA('test') in MySQL/MariaDB
        Assert.Equal("a94a8fe5ccb19ba61c4c0873d391e987982fbbd3", PasswordHash.Sha1Hex("test"));
    }

    [Fact]
    public void Stored_hash_case_does_not_matter()
    {
        Assert.True(PasswordHash.Matches("test", "A94A8FE5CCB19BA61C4C0873D391E987982FBBD3"));
        Assert.False(PasswordHash.Matches("tesT", "a94a8fe5ccb19ba61c4c0873d391e987982fbbd3"));
    }
}
