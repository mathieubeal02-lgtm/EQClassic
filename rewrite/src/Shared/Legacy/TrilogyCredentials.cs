using System.Security.Cryptography;
using System.Text;

namespace EQClassic.Shared.Legacy;

/// <summary>
/// The Trilogy client's OP_LoginInfo credential block: {username[20], password[20]} encrypted with
/// single DES in CBC mode, the key doubling as IV (LS/Login/EQCrypto.cpp, tools/eqbot/eqbot.cpp).
/// Kept so a bridge can accept the original client; the rewrite's own protocol sends
/// <see cref="Login.LoginRequest"/> instead.
/// </summary>
public static class TrilogyCredentials
{
    public const int BlockSize = 40;
    private const int FieldSize = 20;
    private static readonly byte[] Key = [19, 217, 19, 109, 208, 52, 21, 251];

    public static byte[] Encrypt(string username, string password)
    {
        var plain = new byte[BlockSize];
        CopyField(username, plain, 0);
        CopyField(password, plain, FieldSize);
        using var des = Create();
        return des.EncryptCbc(plain, Key, PaddingMode.None);
    }

    /// <summary>Returns false when the block does not have the expected size.</summary>
    public static bool TryDecrypt(ReadOnlySpan<byte> block, out string username, out string password)
    {
        username = password = "";
        if (block.Length < BlockSize)
            return false;
        using var des = Create();
        var plain = des.DecryptCbc(block[..BlockSize], Key, PaddingMode.None);
        username = ReadField(plain, 0);
        password = ReadField(plain, FieldSize);
        return true;
    }

    private static DES Create()
    {
        var des = DES.Create();
        des.Key = Key;
        return des;
    }

    // Like the client: at most 19 characters and a terminating NUL in a 20-byte field.
    private static void CopyField(string value, byte[] target, int offset)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        Array.Copy(bytes, 0, target, offset, Math.Min(bytes.Length, FieldSize - 1));
    }

    private static string ReadField(byte[] plain, int offset)
    {
        int end = Array.IndexOf(plain, (byte)0, offset, FieldSize);
        return Encoding.Latin1.GetString(plain, offset, (end < 0 ? offset + FieldSize : end) - offset);
    }
}
