using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Burst;

/// <summary>
/// Manages local avoidance behavior for entities using spatial hashing and distance-based LOD.
/// This system optimizes performance by only processing entities within a certain range of the player
/// and using a grid-based approach to avoid O(N^2) complexity.
/// </summary>
[UpdateInGroup(typeof(TestUpdateGroup))]
[BurstCompile]
public partial struct AvoidanceSystem : ISystem
{
    private EntityQuery _activeEnemyQuery;
    private EntityQuery _allEnemyQuery;
    private EntityQuery _obstacleQuery;
    private ComponentLookup<LocalTransform> _transformLookup;

    private float _timeSinceLastLOD;

    /// <summary> The size of each spatial hash grid cell. </summary>
    private const float CellSize = 16.0f;
    /// <summary> Frequency of the Level of Detail (LOD) distance check. </summary>
    private const float LodCheckInterval = 0.5f;
    /// <summary> Squared distance threshold for activating avoidance logic. </summary>
    private const float ActivationDistSq = 60f * 60f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // Ensure required singletons exist before updating
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        var builder = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Avoidance, LocalTransform>()
            .WithAllRW<SteeringForce>();
        // Query for entities currently participating in avoidance
        _activeEnemyQuery = state.GetEntityQuery(builder);

        builder.Reset();
        builder.WithAll<Avoidance, LocalTransform>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState);
        // Query for all potential enemies to handle LOD toggling
        _allEnemyQuery = state.GetEntityQuery(builder);
        builder.Dispose();

        _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);

        _obstacleQuery = state.GetEntityQuery(ComponentType.ReadOnly<Obstacle>(), ComponentType.ReadOnly<LocalTransform>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Refresh the lookup to ensure jobs have access to the latest transform data
        _transformLookup.Update(ref state);

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();
        var planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();

        // --- PHASE 1: Level of Detail (LOD) Management ---
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

        // --- PHASE 2: Spatial Hashing ---
        int activeCount = _activeEnemyQuery.CalculateEntityCount();
        int obstacleCount = _obstacleQuery.CalculateEntityCount();

        if (activeCount == 0)
            return;

        var spatialMap = new NativeParallelMultiHashMap<int, AvoidanceData>(
            activeCount + obstacleCount,
            Allocator.TempJob
        );

        var populateEnemiesJob = new PopulateEnemiesSpatialMapJob
        {
            Map = spatialMap.AsParallelWriter(),
            CellSize = CellSize
        };
        state.Dependency = populateEnemiesJob.ScheduleParallel(_activeEnemyQuery, state.Dependency);

        var populateObstaclesJob = new PopulateObstacleSpatialMapJob
        {
            Map = spatialMap.AsParallelWriter(),
            CellSize = CellSize
        };
        state.Dependency = populateObstaclesJob.ScheduleParallel(_obstacleQuery, state.Dependency);

        // --- PHASE 3: Avoidance Calculation ---
        var avoidanceJob = new AvoidanceJob
        {
            Map = spatialMap,
            TransformLookup = _transformLookup,
            PlanetEntity = planetEntity,
            CellSize = CellSize
        };
        state.Dependency = avoidanceJob.ScheduleParallel(_activeEnemyQuery, state.Dependency);

        // Dispose of the map after the avoidance job completes
        state.Dependency = spatialMap.Dispose(state.Dependency);
    }

    /// <summary>
    /// Minimal data required for avoidance calculations stored in the spatial map.
    /// </summary>
    private struct AvoidanceData
    {
        /// <summary> World position of the entity. </summary>
        public float3 Position;
        /// <summary> The entity reference to avoid self-comparison. </summary>
        public Entity Entity;
        public float Radius;
        public bool IsObstacle;
        public float Weight;
    }

    // --- JOBS ---

    [BurstCompile]
    private partial struct LodJob : IJobEntity
    {
        /// <summary>
        /// Toggles the Avoidance component's enabled state based on distance to the player.
        /// </summary>
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
    private partial struct PopulateEnemiesSpatialMapJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, AvoidanceData>.ParallelWriter Map;
        public float CellSize;

        public void Execute(Entity entity, in LocalTransform transform, in Avoidance avoidance)
        {
            var hash = (int)math.hash((int3)math.floor(transform.Position / CellSize));
            Map.Add(hash, new AvoidanceData
            {
                Position = transform.Position,
                Entity = entity,
                Radius = avoidance.Radius,
                Weight = 1.0f,
                IsObstacle = false
            });
        }
    }

    [BurstCompile]
    private partial struct PopulateObstacleSpatialMapJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, AvoidanceData>.ParallelWriter Map;
        public float CellSize;

        public void Execute(Entity entity, in LocalTransform transform, in Obstacle obstacle)
        {
            var hash = (int)math.hash((int3)math.floor(transform.Position / CellSize));
            Map.Add(hash, new AvoidanceData
            {
                Position = transform.Position,
                Entity = entity,
                Radius = obstacle.AvoidanceRadius,
                Weight = obstacle.Weight,
                IsObstacle = true
            });
        }
    }

    [BurstCompile]
    private partial struct AvoidanceJob : IJobEntity
    {
        /// <summary>
        /// Calculates the steering force by checking neighboring cells in the spatial map.
        /// Projects the final force onto the planet's surface tangent plane.
        /// </summary>
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

            // Check the 3x3x3 neighborhood of cells
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
                                if (other.Entity == entity) 
                                    continue;

                                float3 toSelf = transform.Position - other.Position;
                                float distSq = math.lengthsq(toSelf);

                                float combinedRadius = avoidance.Radius + other.Radius;

                                if (distSq < combinedRadius * combinedRadius)
                                {
                                    if (distSq > 0.001f)
                                    {
                                        float dist = math.sqrt(distSq);
                                        float penForce = combinedRadius - dist;
                                        float3 pushDir = toSelf / dist;

                                        //avoidanceForce += (toSelf / distSq) * 5;
                                        avoidanceForce += pushDir * (penForce * other.Weight * 5f);
                                    }
                                    else
                                    {
                                        // Fallback for overlapping entities to prevent zero-length vectors
                                        var rnd = Unity.Mathematics.Random.CreateFromIndex((uint)entity.Index ^ (uint)other.Entity.Index);
                                        avoidanceForce += rnd.NextFloat3Direction() * 5;
                                    }
                                }
                            } while (Map.TryGetNextValue(out other, ref it));
                        }
                    }
                }
            }

            // Constrain the avoidance force to the surface of the planet (tangent plane)
            float3 surfaceNormal = math.normalize(transform.Position - planetCenter);
            avoidanceForce -= surfaceNormal * math.dot(avoidanceForce, surfaceNormal);

            float forceMagnitude = math.length(avoidanceForce);
            if (forceMagnitude > 100f) avoidanceForce = math.normalize(avoidanceForce) * 100f;

            steering.Value = avoidanceForce * avoidance.Weight;
        }
    }
}