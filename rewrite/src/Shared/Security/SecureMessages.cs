using System;
using System.Linq;
using EQClassic.Shared.Protocol;
using LiteNetLib.Utils;

namespace EQClassic.Shared.Security
{
    /// <summary>
    /// Server to client, right after connecting: the server's RSA public key and a fresh nonce the
    /// next <see cref="SecureLoginRequest"/> must contain (a recorded login cannot be replayed).
    /// </summary>
    public sealed record ServerHello(byte[] Modulus, byte[] Exponent, byte[] Nonce) : IMessage
    {
        public MessageType Type => MessageType.ServerHello;

        public string Fingerprint => LoginCrypto.Fingerprint(Modulus, Exponent);

        public void WriteFields(NetDataWriter writer)
        {
            writer.PutBytesWithLength(Modulus);
            writer.PutBytesWithLength(Exponent);
            writer.PutBytesWithLength(Nonce);
        }

        public static ServerHello ReadFields(NetDataReader reader) =>
            new ServerHello(reader.GetBytesWithLength(), reader.GetBytesWithLength(), reader.GetBytesWithLength());

        public bool Equals(ServerHello? other) =>
            other != null && Modulus.SequenceEqual(other.Modulus) && Exponent.SequenceEqual(other.Exponent) && Nonce.SequenceEqual(other.Nonce);

        public override int GetHashCode() => Nonce.Length;
    }

    /// <summary>Client to server: <see cref="LoginCrypto.SealCredentials"/> output. Never contains readable credentials.</summary>
    public sealed record SecureLoginRequest(byte[] SealedCredentials) : IMessage
    {
        public MessageType Type => MessageType.SecureLoginRequest;
        public void WriteFields(NetDataWriter writer) => writer.PutBytesWithLength(SealedCredentials);
        public static SecureLoginRequest ReadFields(NetDataReader reader) => new SecureLoginRequest(reader.GetBytesWithLength());
        public bool Equals(SecureLoginRequest? other) => other != null && SealedCredentials.SequenceEqual(other.SealedCredentials);
        public override int GetHashCode() => SealedCredentials.Length;
    }

    /// <summary>Another message, encrypted and authenticated with the session keys (<see cref="LoginCrypto.Seal"/>).</summary>
    public sealed record Sealed(byte[] Box) : IMessage
    {
        public MessageType Type => MessageType.Sealed;

        public static Sealed Of(IMessage inner, SessionKeys keys) => new Sealed(LoginCrypto.Seal(keys, MessageCodec.Encode(inner)));

        /// <summary>Opens and decodes the inner message; null when tampered with or not decodable.</summary>
        public IMessage? Open(SessionKeys keys)
        {
            if (!LoginCrypto.TryOpen(keys, Box, out var plain))
                return null;
            try
            {
                var inner = MessageCodec.Decode(plain);
                return inner is Sealed ? null : inner; // no nesting
            }
            catch (MessageFormatException)
            {
                return null;
            }
        }

        public void WriteFields(NetDataWriter writer) => writer.PutBytesWithLength(Box);
        public static Sealed ReadFields(NetDataReader reader) => new Sealed(reader.GetBytesWithLength());
        public bool Equals(Sealed? other) => other != null && Box.SequenceEqual(other.Box);
        public override int GetHashCode() => Box.Length;
    }
}
