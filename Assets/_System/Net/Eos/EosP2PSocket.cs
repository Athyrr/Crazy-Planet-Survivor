#if !EOS_DISABLE
using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using Unity.Mathematics;
using Unity.Networking.Transport;
using UnityEngine;

/// <summary>
/// Thin managed wrapper over the EOS P2P interface used as the datagram layer under Unity Transport
/// (see <see cref="EosNetworkInterface"/>). Each remote ProductUserId is mapped to a fake IPv4 endpoint
/// (10.77.x.y:7777) because Unity Transport/Netcode only know about NetworkEndpoints.
/// Packets are sent unreliable-unordered: Unity Transport/Netcode already provide their own reliability,
/// fragmentation and ordering on top. NAT relays are allowed (EOS falls back to Epic relays automatically).
/// </summary>
public sealed class EosP2PSocket : IDisposable
{
    public const string SocketName = "CPSNet";
    public const byte Channel = 0;
    public const int MaxPacketSize = 1170; // P2PInterface MAX_PACKET_SIZE
    private const ushort FakePort = 7777;

    /// <summary>Socket used by the netcode drivers created by <see cref="EosDriverConstructor"/>.</summary>
    public static EosP2PSocket Active;

    public ProductUserId LocalUserId { get; }
    public NetworkEndpoint LocalEndpoint { get; private set; }

    public ulong PacketsSent, PacketsReceived, BytesSent, BytesReceived, SendErrors;

    private readonly P2PInterface _p2p;
    private readonly SocketId _socketId = new SocketId { SocketName = SocketName };
    private readonly Dictionary<string, NetworkEndpoint> _endpointByPeer = new();
    private readonly Dictionary<NetworkEndpoint, ProductUserId> _peerByEndpoint = new();
    private readonly byte[] _buffer = new byte[MaxPacketSize];
    private ulong _connectionRequestNotification;
    private ulong _connectionClosedNotification;
    private int _nextSlot = 1;

    public EosP2PSocket(P2PInterface p2p, ProductUserId localUserId)
    {
        _p2p = p2p;
        LocalUserId = localUserId;
        LocalEndpoint = EndpointFor(localUserId);

        var relay = new SetRelayControlOptions { RelayControl = RelayControl.AllowRelays };
        _p2p.SetRelayControl(ref relay);

        // Accept every incoming connection on our socket: who may join is decided by the lobby / netcode layer.
        var requestOptions = new AddNotifyPeerConnectionRequestOptions { LocalUserId = localUserId, SocketId = _socketId };
        _connectionRequestNotification = _p2p.AddNotifyPeerConnectionRequest(ref requestOptions, null, OnConnectionRequest);

        var closedOptions = new AddNotifyPeerConnectionClosedOptions { LocalUserId = localUserId, SocketId = _socketId };
        _connectionClosedNotification = _p2p.AddNotifyPeerConnectionClosed(ref closedOptions, null, OnConnectionClosed);
    }

    /// <summary>Stable fake endpoint for a peer. The same peer always maps to the same endpoint for the session.</summary>
    public NetworkEndpoint EndpointFor(ProductUserId peer)
    {
        string key = peer.ToString();
        if (_endpointByPeer.TryGetValue(key, out var endpoint))
            return endpoint;

        int slot = _nextSlot++;
        endpoint = NetworkEndpoint.Parse($"10.77.{(slot >> 8) & 0xFF}.{slot & 0xFF}", FakePort);
        _endpointByPeer[key] = endpoint;
        _peerByEndpoint[endpoint] = peer;
        return endpoint;
    }

    public void Send(ref PacketsQueue sendQueue)
    {
        for (int i = 0; i < sendQueue.Count; i++)
        {
            var packet = sendQueue[i];
            if (packet.Length == 0)
                continue;

            if (!_peerByEndpoint.TryGetValue(packet.EndpointRef, out var peer))
                continue;

            int length = math.min(packet.Length, _buffer.Length);
            CopyFromPacket(ref packet, length);

            var options = new SendPacketOptions
            {
                LocalUserId = LocalUserId,
                RemoteUserId = peer,
                SocketId = _socketId,
                Channel = Channel,
                Data = new ArraySegment<byte>(_buffer, 0, length),
                AllowDelayedDelivery = true,
                Reliability = PacketReliability.UnreliableUnordered
            };

            var result = _p2p.SendPacket(ref options);
            if (result == Result.Success)
            {
                PacketsSent++;
                BytesSent += (ulong)length;
            }
            else if (SendErrors++ < 10)
            {
                Debug.LogWarning($"[EOS P2P] SendPacket to {peer} failed: {result}");
            }
        }
    }

    public void Receive(ref PacketsQueue receiveQueue, ref OperationResult receiveResult)
    {
        var options = new ReceivePacketOptions
        {
            LocalUserId = LocalUserId,
            MaxDataSizeBytes = MaxPacketSize,
            RequestedChannel = null
        };

        while (true)
        {
            ProductUserId peer = null;
            SocketId socketId = default;
            var result = _p2p.ReceivePacket(ref options, ref peer, ref socketId, out byte _,
                new ArraySegment<byte>(_buffer), out uint bytesWritten);

            if (result == Result.NotFound)
                return; // queue drained
            if (result != Result.Success)
            {
                Debug.LogWarning($"[EOS P2P] ReceivePacket failed: {result}");
                return;
            }

            if (!receiveQueue.EnqueuePacket(out var packet))
            {
                receiveResult.ErrorCode = (int)Unity.Networking.Transport.Error.StatusCode.NetworkReceiveQueueFull;
                return;
            }

            CopyToPacket(ref packet, (int)bytesWritten);
            packet.EndpointRef = EndpointFor(peer);

            PacketsReceived++;
            BytesReceived += bytesWritten;
        }
    }

    // The project does not allow unsafe code, so payloads are copied 8 bytes at a time through the safe accessors.
    private void CopyFromPacket(ref PacketProcessor packet, int length)
    {
        int i = 0;
        for (; i + 8 <= length; i += 8)
            BitConverter.TryWriteBytes(new Span<byte>(_buffer, i, 8), packet.GetPayloadDataRef<ulong>(i));
        for (; i < length; i++)
            _buffer[i] = packet.GetPayloadDataRef<byte>(i);
    }

    private void CopyToPacket(ref PacketProcessor packet, int length)
    {
        int i = 0;
        for (; i + 8 <= length; i += 8)
            packet.AppendToPayload(BitConverter.ToUInt64(_buffer, i));
        for (; i < length; i++)
            packet.AppendToPayload(_buffer[i]);
    }

    private void OnConnectionRequest(ref OnIncomingConnectionRequestInfo data)
    {
        if (data.SocketId?.SocketName != SocketName)
            return;

        var accept = new AcceptConnectionOptions
        {
            LocalUserId = LocalUserId,
            RemoteUserId = data.RemoteUserId,
            SocketId = _socketId
        };
        var result = _p2p.AcceptConnection(ref accept);
        Debug.Log($"[EOS P2P] Accepted connection from {data.RemoteUserId}: {result}");
    }

    private void OnConnectionClosed(ref OnRemoteConnectionClosedInfo data)
    {
        Debug.Log($"[EOS P2P] Connection with {data.RemoteUserId} closed: {data.Reason}");
    }

    public void Dispose()
    {
        if (_connectionRequestNotification != 0)
            _p2p.RemoveNotifyPeerConnectionRequest(_connectionRequestNotification);
        if (_connectionClosedNotification != 0)
            _p2p.RemoveNotifyPeerConnectionClosed(_connectionClosedNotification);
        _connectionRequestNotification = _connectionClosedNotification = 0;

        var close = new CloseConnectionsOptions { LocalUserId = LocalUserId, SocketId = _socketId };
        _p2p.CloseConnections(ref close);

        if (Active == this)
            Active = null;
    }
}
#endif
