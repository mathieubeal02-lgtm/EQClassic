using EQClassic.Shared.Legacy;

namespace EQClassic.Tests.Legacy;

public class TrilogyCredentialsTests
{
    // openssl enc -des-cbc -K 13d9136dd03415fb -iv 13d9136dd03415fb -nopad over {"test"[20], "test"[20]}
    private const string OpenSslTestTest = "d80d64a463d13bf94b50484025c6e890e7a67a0e0b8659208808b34749739ee4221078cc2f076fdb";

    [Fact]
    public void Encryption_matches_openssl_and_the_trilogy_client_key()
    {
        Assert.Equal(OpenSslTestTest, Convert.ToHexStringLower(TrilogyCredentials.Encrypt("test", "test")));
    }

    [Fact]
    public void Decrypts_a_client_block()
    {
        Assert.True(TrilogyCredentials.TryDecrypt(Convert.FromHexString(OpenSslTestTest), out var user, out var password));
        Assert.Equal(("test", "test"), (user, password));
    }

    [Fact]
    public void Round_trips_and_truncates_to_19_characters_like_the_client()
    {
        var block = TrilogyCredentials.Encrypt("averyveryverylongname", "pw");
        Assert.Equal(TrilogyCredentials.BlockSize, block.Length);
        Assert.True(TrilogyCredentials.TryDecrypt(block, out var user, out var password));
        Assert.Equal("averyveryverylongna", user);
        Assert.Equal("pw", password);
    }

    [Fact]
    public void Short_blocks_are_refused() => Assert.False(TrilogyCredentials.TryDecrypt(new byte[39], out _, out _));
}
