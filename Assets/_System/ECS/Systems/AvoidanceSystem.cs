using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(TestUpdateGroup))]
[BurstCompile]
public partial struct AvoidanceSystem : ISystem
{
    private EntityQuery _activeEnemyQuery;
    private EntityQuery _allEnemyQuery;

    // SAFETY: Use ComponentLookup to read other entities' data inside jobs
    // without forcing the Main Thread to wait.
    private ComponentLookup<LocalTransform> _transformLookup;

    // Config
    private float _timeSinceLastLOD;
    private const float CellSize = 3.0f;
    private const float LodCheckInterval = 0.5f;
    private const float ActivationDistSq = 60f * 60f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        var builder = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Avoidance, LocalTransform>()
            .WithAllRW<SteeringForce>();
        _activeEnemyQuery = state.GetEntityQuery(builder);

        builder.Reset();
        builder.WithAll<Avoidance, LocalTransform>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState);
        _allEnemyQuery = state.GetEntityQuery(builder);
        builder.Dispose();

        _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // 1. CRITICAL: Update the lookup every frame so it's fresh
        _transformLookup.Update(ref state);

        // 2. Get Entities (Lightweight, does not cause sync)
        var playerEntity = SystemAPI.GetSingletonEntity<Player>();
        var planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();

        // --- PHASE 1 : LOD ---
        _timeSinceLastLOD += SystemAPI.Time.DeltaTime;
        if (_timeSinceLastLOD > LodCheckInterval)
        {
            _timeSinceLastLOD = 0;
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();

            var lodJob = new LodJob
            {
                TransformLookup = _transformLookup,
                PlayerEntity = playerEntity,
                DistSqThreshold = ActivationDistSq,
                Ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter()
            };
            state.Dependency = lodJob.ScheduleParallel(_allEnemyQuery, state.Dependency);
        }

        // --- PHASE 2 : Spatial Hashing ---
        int activeCount = _activeEnemyQuery.CalculateEntityCount();
        if (activeCount == 0) return;

        // ALLOCATOR.TEMPJOB: Very fast allocation, safe for job chains.
        // We create it here and schedule its disposal at the end.
        var spatialMap = new NativeParallelMultiHashMap<int, AvoidanceData>(
            activeCount,
            Allocator.TempJob
        );

        var populateJob = new PopulateSpatialMapJob
        {
            Map = spatialMap.AsParallelWriter(),
            CellSize = CellSize
        };
        state.Dependency = populateJob.ScheduleParallel(_activeEnemyQuery, state.Dependency);

        // --- PHASE 3 : Avoidance Calculation ---
        var avoidanceJob = new AvoidanceJob
        {
            // We pass the map as ReadOnly here
            Map = spatialMap,
            TransformLookup = _transformLookup,
            PlanetEntity = planetEntity,
            CellSize = CellSize
        };
        state.Dependency = avoidanceJob.ScheduleParallel(_activeEnemyQuery, state.Dependency);

        // CLEANUP: Dispose of the map Asynchronously.
        // Unity will wait for 'avoidanceJob' to finish before freeing this memory.
        // The Main Thread continues immediately.
        state.Dependency = spatialMap.Dispose(state.Dependency);
    }

    // --- DATA ---
    private struct AvoidanceData
    {
        public float3 Position;
        public Entity Entity;
    }

    // --- JOBS ---

    [BurstCompile]
    private partial struct LodJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        public Entity PlayerEntity;
        public float DistSqThreshold;
        public EntityCommandBuffer.ParallelWriter Ecb;

        public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex, EnabledRefRO<Avoidance> avoidanceEnabled, in LocalTransform transform)
        {
            if (!TransformLookup.HasComponent(PlayerEntity)) return;
            float3 playerPos = TransformLookup[PlayerEntity].Position;

            bool shouldBeActive = math.distancesq(transform.Position, playerPos) < DistSqThreshold;

            if (avoidanceEnabled.ValueRO != shouldBeActive)
            {
                Ecb.SetComponentEnabled<Avoidance>(chunkIndex, entity, shouldBeActive);
            }
        }
    }

    [BurstCompile]
    private partial struct PopulateSpatialMapJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, AvoidanceData>.ParallelWriter Map;
        public float CellSize;

        public void Execute(Entity entity, in LocalTransform transform)
        {
            var hash = (int)math.hash((int3)math.floor(transform.Position / CellSize));
            Map.Add(hash, new AvoidanceData { Position = transform.Position, Entity = entity });
        }
    }

    [BurstCompile]
    private partial struct AvoidanceJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, AvoidanceData> Map;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        public Entity PlanetEntity;
        public float CellSize;

        public void Execute(Entity entity, in Avoidance avoidance, in LocalTransform transform, ref SteeringForce steering)
        {
            if (!TransformLookup.HasComponent(PlanetEntity)) return;
            float3 planetCenter = TransformLookup[PlanetEntity].Position;

            float3 avoidanceForce = float3.zero;
            int3 centerCell = (int3)math.floor(transform.Position / CellSize);

            // Loop unrolling hint for Burst (optional but often helps)
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        int3 neighborCell = centerCell + new int3(x, y, z);
                        int hash = (int)math.hash(neighborCell);

                        if (Map.TryGetFirstValue(hash, out AvoidanceData other, out var it))
                        {
                            do
                            {
                                if (other.Entity == entity) continue;

                                float3 toSelf = transform.Position - other.Position;
                                float distSq = math.lengthsq(toSelf);

                                if (distSq < avoidance.Radius * avoidance.Radius)
                                {
                                    if (distSq > 0.001f)
                                    {
                                        avoidanceForce += (toSelf / distSq) * 4;
                                    }
                                    else
                                    {
                                        var rnd = Unity.Mathematics.Random.CreateFromIndex((uint)entity.Index ^ (uint)other.Entity.Index);
                                        avoidanceForce += rnd.NextFloat3Direction() * 4;
                                    }
                                    // Removed 'goto' for cleaner flow, but logic remains
                                }
                            } while (Map.TryGetNextValue(out other, ref it));
                        }
                    }
                }
            }

            float3 surfaceNormal = math.normalize(transform.Position - planetCenter);
            avoidanceForce -= surfaceNormal * math.dot(avoidanceForce, surfaceNormal);

            steering.Value = avoidanceForce * avoidance.Weight;
        }
    }
}