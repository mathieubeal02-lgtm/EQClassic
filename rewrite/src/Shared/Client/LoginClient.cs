using System;
using System.Collections.Generic;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;
using EQClassic.Shared.Security;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EQClassic.Shared.Client
{
    public enum ConnectionState { Disconnected, Connecting, Connected }

    /// <summary>
    /// Client side of the login protocol, for the Unity client and tools. No threads of its own:
    /// call <see cref="Poll"/> from the main loop (Unity's Update); received messages are returned
    /// in order. Built on the same contracts as the server (EQClassic.Shared.Login).
    /// </summary>
    public sealed class LoginClient : IDisposable
    {
        private readonly EventBasedNetListener _listener = new EventBasedNetListener();
        private readonly NetManager _net;
        private readonly NetDataWriter _writer = new NetDataWriter();
        private readonly List<IMessage> _inbox = new List<IMessage>();
        private NetPeer? _peer;
        private ServerHello? _hello;
        private SessionKeys? _pendingKeys;
        private SessionKeys? _keys;

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        /// <summary>Why the last connection ended (refused key, timeout, server kick...).</summary>
        public string? DisconnectReason { get; private set; }

        /// <summary>
        /// When set, the client refuses a server whose key fingerprint differs (printed by the
        /// server at start-up): protects the password against a man in the middle.
        /// </summary>
        public string? ExpectedServerFingerprint { get; set; }

        /// <summary>Fingerprint of the key the server presented, once <see cref="IsReadyToLogin"/>.</summary>
        public string? ServerFingerprint => _hello?.Fingerprint;

        /// <summary>Type of every message as it arrived on the wire, before unsealing (diagnostics, tests).</summary>
        public event Action<MessageType>? RawMessageReceived;

        /// <summary>The server's key and nonce arrived: <see cref="Login"/> can be called.</summary>
        public bool IsReadyToLogin => _hello != null && State == ConnectionState.Connected;

        /// <summary>A successful login established session keys (sealed messages can be read).</summary>
        public bool IsLoggedIn => _keys != null;

        public LoginClient()
        {
            _net = new NetManager(_listener) { AutoRecycle = true };
            _listener.PeerConnectedEvent += _ => State = ConnectionState.Connected;
            _listener.PeerDisconnectedEvent += (_, info) =>
            {
                State = ConnectionState.Disconnected;
                DisconnectReason ??= info.Reason.ToString();
                _peer = null;
                _hello = null;
                _pendingKeys = _keys = null;
            };
            _listener.NetworkReceiveEvent += (_, reader, _, _) =>
            {
                try
                {
                    Receive(MessageCodec.Read(reader));
                }
                catch (MessageFormatException e)
                {
                    DisconnectReason = "malformed message from server: " + e.Message;
                    _peer?.Disconnect();
                }
            };
            _net.Start();
        }

        /// <summary>Starts connecting; watch <see cref="State"/> while calling <see cref="Poll"/>.</summary>
        public void Connect(string host, int port = ProtocolInfo.DefaultLoginPort, string connectionKey = ProtocolInfo.ConnectionKey)
        {
            DisconnectReason = null;
            _hello = null;
            _pendingKeys = _keys = null;
            State = ConnectionState.Connecting;
            _peer = _net.Connect(host, port, connectionKey);
            if (_peer == null)
            {
                State = ConnectionState.Disconnected;
                DisconnectReason = "could not start connecting to " + host + ":" + port;
            }
        }

        /// <summary>
        /// Logs in with credentials encrypted for the server's key (<see cref="SecureLoginRequest"/>);
        /// the answer is a <see cref="LoginResponse"/> returned by <see cref="Poll"/>.
        /// </summary>
        public void Login(string username, string password)
        {
            if (_hello == null)
                throw new InvalidOperationException("the server has not sent its key yet (wait for IsReadyToLogin)");
            var secret = LoginCrypto.RandomBytes(LoginCrypto.SecretSize);
            var sealedCredentials = LoginCrypto.SealCredentials(_hello.Modulus, _hello.Exponent, _hello.Nonce, secret, username, password);
            _pendingKeys = LoginCrypto.DeriveSessionKeys(secret, _hello.Nonce);
            Send(new SecureLoginRequest(sealedCredentials));
        }

        private void Receive(IMessage message)
        {
            RawMessageReceived?.Invoke(message.Type);
            switch (message)
            {
                case ServerHello hello:
                    if (ExpectedServerFingerprint != null
                        && !string.Equals(ExpectedServerFingerprint, hello.Fingerprint, StringComparison.OrdinalIgnoreCase))
                    {
                        DisconnectReason = "server key fingerprint " + hello.Fingerprint + " does not match the expected one";
                        _peer?.Disconnect();
                        return;
                    }
                    _hello = hello;
                    break;
                case LoginResponse response:
                    _keys = response.Result == LoginResult.Success ? _pendingKeys : null;
                    _pendingKeys = null;
                    break;
                case Sealed box:
                    var inner = _keys == null ? null : box.Open(_keys);
                    if (inner == null)
                    {
                        DisconnectReason = "could not open a sealed message from the server";
                        _peer?.Disconnect();
                        return;
                    }
                    _inbox.Add(inner);
                    return;
            }
            _inbox.Add(message);
        }

        public void Send(IMessage message)
        {
            if (_peer == null || State != ConnectionState.Connected)
                throw new InvalidOperationException("not connected");
            _writer.Reset();
            MessageCodec.Write(_writer, message);
            _peer.Send(_writer, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>Sends pre-encoded bytes (tests and diagnostics).</summary>
        public void SendRaw(byte[] data)
        {
            if (_peer == null || State != ConnectionState.Connected)
                throw new InvalidOperationException("not connected");
            _peer.Send(data, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>Processes network events and returns the messages received since the last call.</summary>
        public IReadOnlyList<IMessage> Poll()
        {
            _net.PollEvents();
            if (_inbox.Count == 0)
                return Array.Empty<IMessage>();
            var received = _inbox.ToArray();
            _inbox.Clear();
            return received;
        }

        public void Disconnect() => _peer?.Disconnect();

        public void Dispose() => _net.Stop();
    }
}
