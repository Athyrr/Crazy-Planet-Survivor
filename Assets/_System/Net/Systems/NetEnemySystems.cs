using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Server: keeps exactly <see cref="NetEnemyTarget.Count"/> enemy ghosts alive. New enemies spawn on a ring
/// around a random player (or the planet pole if nobody is connected yet), extra ones are destroyed.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct NetEnemySpawnSystem : ISystem
{
    private EntityQuery _enemyQuery;
    private EntityQuery _playerQuery;
    private Random _random;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetTestConfig>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        _enemyQuery = SystemAPI.QueryBuilder().WithAll<NetEnemy>().Build();
        _playerQuery = SystemAPI.QueryBuilder().WithAll<NetPlayer, LocalTransform>().Build();
        _random = new Random(0x6E657421);

        var target = state.EntityManager.CreateEntity(typeof(NetEnemyTarget));
        state.EntityManager.SetName(target, "NetEnemyTarget");
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        int target = math.max(0, SystemAPI.GetSingleton<NetEnemyTarget>().Count);
        int current = _enemyQuery.CalculateEntityCount();
        if (current == target)
            return;

        if (current > target)
        {
            var enemies = _enemyQuery.ToEntityArray(Allocator.Temp);
            state.EntityManager.DestroyEntity(enemies.GetSubArray(target, current - target));
            return;
        }

        var config = SystemAPI.GetSingleton<NetTestConfig>();
        var planet = SystemAPI.GetSingleton<PlanetData>();
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var players = _playerQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        var filter = new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape };

        int toSpawn = math.min(target - current, math.max(1, config.MaxSpawnPerTick));
        var spawned = state.EntityManager.Instantiate(config.EnemyPrefab, toSpawn, Allocator.Temp);
        for (int i = 0; i < spawned.Length; i++)
        {
            float3 anchor = players.Length > 0
                ? players[_random.NextInt(players.Length)].Position
                : planet.Center + new float3(0, 1, 0) * planet.Radius;

            if (!PlanetUtils.GetRandomPointOnSurface(ref collisionWorld, ref _random, anchor, planet.Center,
                    config.EnemySpawnMinDistance, config.EnemySpawnMaxDistance, ref filter, out var position, out var normal))
            {
                // Ray missed (e.g. collider not built yet): random point on the bare sphere.
                normal = _random.NextFloat3Direction();
                position = planet.Center + normal * planet.Radius;
            }

            float3 forward = math.normalize(math.cross(_random.NextFloat3Direction(), normal));
            state.EntityManager.SetComponentData(spawned[i],
                LocalTransform.FromPositionRotation(position, quaternion.LookRotationSafe(forward, normal)));

            // Small speed variance so the horde doesn't move as one rigid blob.
            var enemy = state.EntityManager.GetComponentData<NetEnemy>(spawned[i]);
            enemy.Speed *= _random.NextFloat(0.8f, 1.2f);
            state.EntityManager.SetComponentData(spawned[i], enemy);
        }
    }
}

/// <summary>
/// Server: moves every enemy towards its nearest player on the sphere (multi-player replacement for the solo
/// FollowTargetMovement + single Player target). Clients only receive the resulting transforms (interpolated).
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PredictedSimulationSystemGroup))]
[UpdateAfter(typeof(NetEnemySpawnSystem))]
[BurstCompile]
public partial struct NetEnemyMoveSystem : ISystem
{
    private EntityQuery _playerQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetEnemy>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        _playerQuery = SystemAPI.QueryBuilder().WithAll<NetPlayer, LocalTransform>().Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (_playerQuery.IsEmpty)
            return;

        var players = _playerQuery.ToComponentDataArray<LocalTransform>(state.WorldUpdateAllocator);
        state.Dependency = new MoveJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            PlanetCenter = SystemAPI.GetSingleton<PlanetData>().Center,
            CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
            Players = players
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct MoveJob : IJobEntity
    {
        public float DeltaTime;
        public float3 PlanetCenter;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public NativeArray<LocalTransform> Players;

        private const float SNAP_DISTANCE = 50f;
        private const float VERT_SNAP_SPEED = 20f;
        private const float ROT_SMOOTH_SPEED = 10f;

        public void Execute(ref LocalTransform transform, in NetEnemy enemy)
        {
            float3 target = Players[0].Position;
            float bestSq = math.distancesq(target, transform.Position);
            for (int i = 1; i < Players.Length; i++)
            {
                float d = math.distancesq(Players[i].Position, transform.Position);
                if (d < bestSq) { bestSq = d; target = Players[i].Position; }
            }

            if (bestSq <= enemy.StopDistance * enemy.StopDistance)
                return;

            float3 up = math.normalize(transform.Position - PlanetCenter);
            PlanetUtils.ProjectDirectionOnSurface(target - transform.Position, up, out float3 tangent);
            if (math.lengthsq(tangent) < 0.001f)
                return;

            float3 desired = transform.Position + tangent * (enemy.Speed * DeltaTime);
            var filter = new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape };
            if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, desired, PlanetCenter, filter, SNAP_DISTANCE, out var hit))
            {
                // Same vertical smoothing as the solo follow job: lateral motion applied fully, height eased.
                float3 toHit = hit.Position - transform.Position;
                float vertical = math.dot(toHit, up);
                float3 horizontal = toHit - up * vertical;
                float vertStep = math.abs(vertical) > 1.5f ? vertical : vertical * math.min(1f, DeltaTime * VERT_SNAP_SPEED);
                transform.Position += horizontal + up * vertStep;
                up = hit.SurfaceNormal;
            }
            else
            {
                transform.Position = desired;
            }

            var targetRotation = quaternion.LookRotationSafe(tangent, up);
            transform.Rotation = math.slerp(transform.Rotation, targetRotation, math.saturate(DeltaTime * ROT_SMOOTH_SPEED));
        }
    }
}
