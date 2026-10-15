#if !EOS_DISABLE
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

/// <summary>
/// Unity Transport network interface that moves datagrams over EOS P2P instead of UDP sockets.
/// EOS is a managed, main-thread API, so send/receive run synchronously on the main thread when the driver
/// schedules them (the dependency is completed first) and an already-completed handle is returned.
/// Managed interfaces must be wrapped with <c>WrapToUnmanaged()</c> before being given to a NetworkDriver.
/// </summary>
public struct EosNetworkInterface : INetworkInterface
{
    private EosP2PSocket _socket;

    public EosNetworkInterface(EosP2PSocket socket)
    {
        _socket = socket;
    }

    public NetworkEndpoint LocalEndpoint => _socket != null ? _socket.LocalEndpoint : default;

    public int Initialize(ref NetworkSettings settings, ref int packetPadding)
    {
        if (_socket == null)
        {
            Debug.LogError("[EOS P2P] EosNetworkInterface created without an EosP2PSocket (EOS not logged in?).");
            return -1;
        }
        return 0;
    }

    public int Bind(NetworkEndpoint endpoint) => 0; // the EOS socket is implicitly bound to the local ProductUserId
    public int Listen() => 0;                      // incoming P2P connection requests are always accepted

    public JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dep)
    {
        dep.Complete();
        _socket.Receive(ref arguments.ReceiveQueue, ref arguments.ReceiveResult);
        return default;
    }

    public JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dep)
    {
        dep.Complete();
        _socket.Send(ref arguments.SendQueue);
        return default;
    }

    public void Dispose() { }
}

/// <summary>
/// Netcode driver constructor used when the net test runs over EOS: one EOS P2P driver for remote peers, plus the
/// default in-process IPC driver on the server so the host's own client never goes through the network.
/// Install it with <c>NetworkStreamReceiveSystem.DriverConstructor = new EosDriverConstructor()</c> before creating
/// the client/server worlds.
/// </summary>
public class EosDriverConstructor : INetworkStreamDriverConstructor
{
    private static NetworkSettings WithEosMtu(NetworkSettings settings)
    {
        // EOS P2P packets are limited to 1170 bytes; tell Unity Transport so it never builds larger datagrams.
        settings.TryGet<NetworkConfigParameter>(out var config);
        config.maxMessageSize = EosP2PSocket.MaxPacketSize;
        settings.AddRawParameterStruct(ref config);
        return settings;
    }

    public void CreateClientDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
    {
        var socket = EosP2PSocket.Active;
        bool inProcessServer = ClientServerBootstrap.ServerWorld != null && ClientServerBootstrap.ServerWorld.IsCreated;
        if (inProcessServer)
        {
            // Host: talk to the local server through IPC, exactly like the default constructor.
            DefaultDriverBuilder.RegisterClientIpcDriver(world, ref driverStore, netDebug, DefaultDriverBuilder.GetNetworkClientSettings());
            return;
        }

        var settings = WithEosMtu(DefaultDriverBuilder.GetNetworkClientSettings());
        var instance = new NetworkDriverStore.NetworkDriverInstance
        {
            driver = NetworkDriver.Create(new EosNetworkInterface(socket).WrapToUnmanaged(), settings)
        };
        DefaultDriverBuilder.CreateClientPipelines(ref instance);
        driverStore.RegisterDriver(TransportType.Socket, instance);
    }

    public void CreateServerDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
    {
        DefaultDriverBuilder.RegisterServerIpcDriver(world, ref driverStore, netDebug, DefaultDriverBuilder.GetNetworkServerSettings());

        var settings = WithEosMtu(DefaultDriverBuilder.GetNetworkServerSettings());
        var instance = new NetworkDriverStore.NetworkDriverInstance
        {
            driver = NetworkDriver.Create(new EosNetworkInterface(EosP2PSocket.Active).WrapToUnmanaged(), settings)
        };
        DefaultDriverBuilder.CreateServerPipelines(ref instance);
        driverStore.RegisterDriver(TransportType.Socket, instance);
    }
}
#endif
