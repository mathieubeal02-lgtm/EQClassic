using System.Security.Cryptography;
using EQClassic.Shared.Login;

namespace EQClassic.Server.Login;

/// <summary>
/// World hand-off keys. Same shape as the legacy key (15 characters from [0-9A-Za-z],
/// Client::SendSessionKey) but drawn from a cryptographic generator instead of rand().
/// </summary>
public static class WorldKeys
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string New() => RandomNumberGenerator.GetString(Alphabet, LoginLimits.SessionKeyLength);

    public static bool IsWellFormed(string key) =>
        key.Length == LoginLimits.SessionKeyLength && key.All(c => Alphabet.Contains(c));
}
