using System;
using System.Collections.Generic;
using EQClassic.Shared.Protocol;
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

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        /// <summary>Why the last connection ended (refused key, timeout, server kick...).</summary>
        public string? DisconnectReason { get; private set; }

        public LoginClient()
        {
            _net = new NetManager(_listener) { AutoRecycle = true };
            _listener.PeerConnectedEvent += _ => State = ConnectionState.Connected;
            _listener.PeerDisconnectedEvent += (_, info) =>
            {
                State = ConnectionState.Disconnected;
                DisconnectReason = info.Reason.ToString();
                _peer = null;
            };
            _listener.NetworkReceiveEvent += (_, reader, _, _) =>
            {
                try
                {
                    _inbox.Add(MessageCodec.Read(reader));
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
            State = ConnectionState.Connecting;
            _peer = _net.Connect(host, port, connectionKey);
            if (_peer == null)
            {
                State = ConnectionState.Disconnected;
                DisconnectReason = "could not start connecting to " + host + ":" + port;
            }
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
