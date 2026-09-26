using System.Security.Cryptography;
using System.Text;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;
using EQClassic.Shared.Security;

namespace EQClassic.Tests.Protocol;

public class LoginCryptoTests
{
    private static readonly RSA Key = RSA.Create(2048);
    private static readonly RSAParameters Public = Key.ExportParameters(false);

    [Fact]
    public void Credentials_round_trip_through_the_server_key()
    {
        var nonce = LoginCrypto.RandomBytes(LoginCrypto.NonceSize);
        var secret = LoginCrypto.RandomBytes(LoginCrypto.SecretSize);
        var box = LoginCrypto.SealCredentials(Public.Modulus!, Public.Exponent!, nonce, secret, "tést", "pa55");

        Assert.True(LoginCrypto.TryOpenCredentials(Key, box, out var n, out var s, out var user, out var password));
        Assert.Equal(nonce, n);
        Assert.Equal(secret, s);
        Assert.Equal(("tést", "pa55"), (user, password));
    }

    [Fact]
    public void The_login_packet_on_the_wire_does_not_contain_the_password()
    {
        var box = LoginCrypto.SealCredentials(Public.Modulus!, Public.Exponent!, new byte[32], new byte[32], "test", "hunter2");
        var wire = MessageCodec.Encode(new SecureLoginRequest(box));
        Assert.Equal(-1, wire.AsSpan().IndexOf(Encoding.UTF8.GetBytes("hunter2")));
        Assert.Equal(-1, wire.AsSpan().IndexOf(Encoding.UTF8.GetBytes("test")));
    }

    [Fact]
    public void Another_key_cannot_open_credentials()
    {
        using var other = RSA.Create(2048);
        var box = LoginCrypto.SealCredentials(Public.Modulus!, Public.Exponent!, new byte[32], new byte[32], "a", "b");
        Assert.False(LoginCrypto.TryOpenCredentials(other, box, out _, out _, out _, out _));
    }

    [Fact]
    public void Sealed_messages_round_trip_and_detect_tampering()
    {
        var keys = LoginCrypto.DeriveSessionKeys(LoginCrypto.RandomBytes(32), LoginCrypto.RandomBytes(32));
        var inner = new PlayResponse(true, "", "abcdefghijklmno", "10.0.0.5", 9000);
        var box = Sealed.Of(inner, keys);

        Assert.Equal(inner, box.Open(keys));

        var tampered = (byte[])box.Box.Clone();
        tampered[20] ^= 1;
        Assert.Null(new Sealed(tampered).Open(keys));

        var otherKeys = LoginCrypto.DeriveSessionKeys(LoginCrypto.RandomBytes(32), LoginCrypto.RandomBytes(32));
        Assert.Null(box.Open(otherKeys));
    }

    [Fact]
    public void Session_keys_depend_on_secret_and_nonce()
    {
        var secret = LoginCrypto.RandomBytes(32);
        var a = LoginCrypto.DeriveSessionKeys(secret, new byte[32]);
        var b = LoginCrypto.DeriveSessionKeys(secret, Enumerable.Repeat((byte)1, 32).ToArray());
        Assert.NotEqual(a.Encryption, b.Encryption);
        Assert.NotEqual(a.Encryption, a.Authentication);
    }

    [Fact]
    public void Secure_messages_round_trip_through_the_codec()
    {
        var hello = new ServerHello(Public.Modulus!, Public.Exponent!, LoginCrypto.RandomBytes(32));
        Assert.Equal(hello, MessageCodec.Decode(MessageCodec.Encode(hello)));
        Assert.Equal(64, hello.Fingerprint.Length);
    }
}
