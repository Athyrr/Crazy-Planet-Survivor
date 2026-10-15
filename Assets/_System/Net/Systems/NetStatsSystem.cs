using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>Per-world bandwidth snapshot, refreshed once per second by <see cref="NetStatsSystem"/>.</summary>
public class NetWorldStats
{
    public string World;
    public bool IsServer;
    public float RxKBps;        // kilobytes per second received (all drivers of this world, transport level, incl. headers)
    public float TxKBps;        // kilobytes per second sent
    public float RemoteRxKBps;  // same, network drivers only (excludes the host's in-process IPC client)
    public float RemoteTxKBps;
    public float RxPacketsPerSec;
    public float TxPacketsPerSec;
    public ulong RxTotalBytes;
    public ulong TxTotalBytes;
    public int Connections;
    public int Enemies;         // server: simulated, client: ghosts received
    public int Players;
    public float RttMs;         // client only
    public string Drivers;

    internal ulong LastRx, LastTx, LastRxPackets, LastTxPackets, LastRemoteRx, LastRemoteTx;
    internal float LastSampleTime = -1f;
}

/// <summary>
/// Debug bandwidth meter for the net test. Reads Unity Transport driver statistics (bytes actually handed to the
/// network interface, so it is the same number for UDP and the EOS P2P interface) once per second.
/// For per-ghost / per-component breakdown use the Netcode NetDbg tool (Multiplayer > Window: NetDbg).
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
public partial class NetStatsSystem : SystemBase
{
    public static readonly Dictionary<string, NetWorldStats> ByWorld = new();

    private EntityQuery _enemyQuery;
    private EntityQuery _playerQuery;
    private EntityQuery _connectionQuery;
    private NetWorldStats _stats;

    protected override void OnCreate()
    {
        RequireForUpdate<NetworkStreamDriver>();
        _enemyQuery = SystemAPI.QueryBuilder().WithAll<NetEnemy>().Build();
        _playerQuery = SystemAPI.QueryBuilder().WithAll<NetPlayer>().Build();
        _connectionQuery = SystemAPI.QueryBuilder().WithAll<NetworkId>().Build();
        _stats = new NetWorldStats { World = World.Name, IsServer = World.IsServer() };
        ByWorld[World.Name] = _stats;
    }

    protected override void OnDestroy()
    {
        ByWorld.Remove(World.Name);
    }

    protected override void OnUpdate()
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (_stats.LastSampleTime >= 0f && now - _stats.LastSampleTime < 1f)
            return;

        // The driver is updated by netcode jobs; make sure they are done before reading its counters.
        var driverStore = SystemAPI.GetSingletonRW<NetworkStreamDriver>().ValueRO.DriverStore;
        CompleteDependency();

        ulong rx = 0, tx = 0, rxp = 0, txp = 0, remoteRx = 0, remoteTx = 0;
        string drivers = "";
        for (int i = driverStore.FirstDriver; i < driverStore.LastDriver; i++)
        {
            var s = driverStore.GetNetworkDriver(i).GetStatistics();
            var type = driverStore.GetDriverType(i);
            rx += s.RxTotalBytes;
            tx += s.TxTotalBytes;
            rxp += s.RxTotalPackets;
            txp += s.TxTotalPackets;
            if (type != TransportType.IPC)
            {
                remoteRx += s.RxTotalBytes;
                remoteTx += s.TxTotalBytes;
            }
            drivers += (drivers.Length > 0 ? "+" : "") + type;
        }

        if (_stats.LastSampleTime >= 0f)
        {
            float dt = now - _stats.LastSampleTime;
            _stats.RxKBps = (rx - _stats.LastRx) / 1024f / dt;
            _stats.TxKBps = (tx - _stats.LastTx) / 1024f / dt;
            _stats.RemoteRxKBps = (remoteRx - _stats.LastRemoteRx) / 1024f / dt;
            _stats.RemoteTxKBps = (remoteTx - _stats.LastRemoteTx) / 1024f / dt;
            _stats.RxPacketsPerSec = (rxp - _stats.LastRxPackets) / dt;
            _stats.TxPacketsPerSec = (txp - _stats.LastTxPackets) / dt;
        }

        _stats.LastRx = rx;
        _stats.LastTx = tx;
        _stats.LastRxPackets = rxp;
        _stats.LastTxPackets = txp;
        _stats.LastRemoteRx = remoteRx;
        _stats.LastRemoteTx = remoteTx;
        _stats.LastSampleTime = now;
        _stats.RxTotalBytes = rx;
        _stats.TxTotalBytes = tx;
        _stats.Drivers = drivers;
        _stats.Connections = _connectionQuery.CalculateEntityCount();
        _stats.Enemies = _enemyQuery.CalculateEntityCount();
        _stats.Players = _playerQuery.CalculateEntityCount();
        _stats.RttMs = !_stats.IsServer && SystemAPI.TryGetSingleton<NetworkSnapshotAck>(out var ack) ? ack.EstimatedRTT : 0f;
    }
}
