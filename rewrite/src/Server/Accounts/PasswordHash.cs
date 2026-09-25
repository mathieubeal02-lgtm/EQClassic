using System.Security.Cryptography;
using System.Text;

namespace EQClassic.Server.Accounts;

/// <summary>
/// The legacy login server compares <c>password = sha('...')</c> in SQL: lowercase hex SHA-1 of the
/// typed password. Kept identical so existing login_accounts rows keep working.
/// </summary>
public static class PasswordHash
{
    public static string Sha1Hex(string password) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.Latin1.GetBytes(password)));

    public static bool Matches(string password, string storedSha1Hex)
    {
        var computed = Encoding.ASCII.GetBytes(Sha1Hex(password));
        var stored = Encoding.ASCII.GetBytes(storedSha1Hex.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }
}
