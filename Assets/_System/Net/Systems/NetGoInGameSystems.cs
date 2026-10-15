using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Client: once a connection has a NetworkId and the planet sub scene (ghost prefabs) is loaded,
/// mark it in game and ask the server for a player.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
[BurstCompile]
public partial struct NetGoInGameClientSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        var builder = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<NetworkId>()
            .WithNone<NetworkStreamInGame>();
        state.RequireForUpdate(state.GetEntityQuery(builder));
        state.RequireForUpdate<NetTestConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>()
                     .WithNone<NetworkStreamInGame>().WithEntityAccess())
        {
            ecb.AddComponent<NetworkStreamInGame>(connection);
            var request = ecb.CreateEntity();
            ecb.AddComponent<NetGoInGameRequest>(request);
            ecb.AddComponent(request, new SendRpcCommandRequest { TargetConnection = connection });
        }
        ecb.Playback(state.EntityManager);
    }
}

/// <summary>
/// Server: answers <see cref="NetGoInGameRequest"/> by spawning an owner-predicted player ghost for the
/// connection. The player is linked to the connection entity so it is destroyed on disconnect.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[BurstCompile]
public partial struct NetGoInGameServerSystem : ISystem
{
    private ComponentLookup<NetworkId> _networkIdLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        var builder = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<NetGoInGameRequest, ReceiveRpcCommandRequest>();
        state.RequireForUpdate(state.GetEntityQuery(builder));
        state.RequireForUpdate<NetTestConfig>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _networkIdLookup.Update(ref state);
        var config = SystemAPI.GetSingleton<NetTestConfig>();
        var planet = SystemAPI.GetSingleton<PlanetData>();
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        var ecb = new EntityCommandBuffer(Allocator.Temp);
        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
                     .WithAll<NetGoInGameRequest>().WithEntityAccess())
        {
            var connection = request.ValueRO.SourceConnection;
            ecb.DestroyEntity(requestEntity);
            if (!_networkIdLookup.HasComponent(connection))
                continue;

            int networkId = _networkIdLookup[connection].Value;
            ecb.AddComponent<NetworkStreamInGame>(connection);

            // Spread players around the planet "north pole" so they don't spawn inside each other.
            float angle = networkId * 2.4f;
            float3 up = math.normalize(new float3(math.sin(angle) * 0.08f, 1f, math.cos(angle) * 0.08f));
            float3 position = planet.Center + up * planet.Radius;
            if (PlanetUtils.SnapToSurfaceRaycast(ref collisionWorld, position, planet.Center,
                    new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape },
                    planet.Radius, out var hit))
            {
                position = hit.Position;
                up = hit.SurfaceNormal;
            }

            float3 forward = math.normalize(math.cross(new float3(1, 0, 0), up));
            var player = ecb.Instantiate(config.PlayerPrefab);
            ecb.SetComponent(player, LocalTransform.FromPositionRotation(position, quaternion.LookRotationSafe(forward, up)));
            ecb.SetComponent(player, new GhostOwner { NetworkId = networkId });
            ecb.AppendToBuffer(connection, new LinkedEntityGroup { Value = player });
        }
        ecb.Playback(state.EntityManager);
    }
}
