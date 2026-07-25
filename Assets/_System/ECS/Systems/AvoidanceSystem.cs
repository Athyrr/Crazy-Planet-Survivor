using _System.Settings;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Burst;
using Unity.Jobs;

/// <summary>
/// Local avoidance via spatial hashing + distance LOD, keeping the cost near O(N) instead of O(N^2).
///
/// Two grids keyed by per-entity avoidance radius: a FINE grid for the numerous small-radius entities
/// and a COARSE grid for the few large-radius ones plus static obstacles. The fine grid's cell size is
/// fixed by the routing threshold, so the common small population is never forced onto an
/// obstacle-sized cell.
///
/// Repulsion is split by MASS ratio: a heavy entity shoves the horde aside without being shoved back
/// (a very heavy entity / obstacle is effectively immovable), which replaces the old per-role weight hack.
/// </summary>
[UpdateInGroup(typeof(CustomUpdateGroup))]
public partial struct AvoidanceSystem : ISystem
{
    private EntityQuery _activeEnemyQuery;
    private EntityQuery _allEnemyQuery;
    private EntityQuery _obstaclesQuery;
    private ComponentLookup<LocalTransform> _transformLookup;

    private float _timeSinceLastLOD;

    /// <summary> Frequency of the Level of Detail (LOD) distance check. </summary>
    private const float LodCheckInterval = 0.5f;
    /// <summary> Squared distance threshold for activating avoidance logic. </summary>
    private const float ActivationDistSq = 60f * 60f;

    public void OnCreate(ref SystemState state)
    {
        // Ensure required singletons exist before updating
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        var builder = new EntityQueryBuilder(Allocator.Temp);

        // Query for entities currently participating in avoidance
        builder.WithAll<Avoidance, LocalTransform>()
         .WithAllRW<SteeringForce>();
        _activeEnemyQuery = state.GetEntityQuery(builder);

        // All enemies query
        builder.Reset();
        builder.WithAll<Avoidance, LocalTransform>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState);
        _allEnemyQuery = state.GetEntityQuery(builder);

        // Obstacles query
        builder.Reset();
        builder.WithAll<Obstacle, LocalTransform>();
        _obstaclesQuery = state.GetEntityQuery(builder);

        builder.Dispose();

        _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
    }

    // Not Burst-compiled: reads the managed CpAvoidanceSettings so the global params are live-tunable
    // in play. The heavy per-entity work stays in the Burst jobs, which receive a blittable snapshot.
    public void OnUpdate(ref SystemState state)
    {
        // Refresh the lookup to ensure jobs have access to the latest transform data
        _transformLookup.Update(ref state);

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();
        var planetData = SystemAPI.GetSingleton<PlanetData>();

        // Live-tunable global config (falls back to safe defaults if no settings asset exists).
        AvoidanceConfig config = CpAvoidanceSettings.I != null
            ? CpAvoidanceSettings.I.BuildConfig()
            : AvoidanceConfig.Default;

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
        int obstacleCount = _obstaclesQuery.CalculateEntityCount();

        if (activeCount == 0)
            return;

        // Fine grid: small-radius enemies (<= activeCount). Coarse grid: large-radius enemies + every
        // obstacle (<= activeCount + obstacleCount). Both bounds are safe upper bounds so neither map
        // reallocates.
        var fineMap = new NativeParallelMultiHashMap<int, AvoidanceData>(
            activeCount,
            Allocator.TempJob
        );
        var coarseMap = new NativeParallelMultiHashMap<int, AvoidanceData>(
            activeCount + obstacleCount,
            Allocator.TempJob
        );

        // Enemies route themselves to the fine or coarse map by radius.
        var populateEnemiesHandle = new PopulateEnemiesSpatialMapJob
        {
            FineMap = fineMap.AsParallelWriter(),
            CoarseMap = coarseMap.AsParallelWriter(),
            Config = config
        }.ScheduleParallel(_activeEnemyQuery, state.Dependency);

        // Obstacles also live in the coarse map, so this must run AFTER the enemy populate (both write
        // the coarse map's ParallelWriter — concurrent writes to one writer would race).
        var populateObstaclesHandle = new PopulateObstacleSpatialMapJob
        {
            CoarseMap = coarseMap.AsParallelWriter(),
            CoarseCellSize = config.CoarseCellSize,
            ObstacleMass = config.ObstacleMass
        }.ScheduleParallel(_obstaclesQuery, populateEnemiesHandle);

        state.Dependency = populateObstaclesHandle;

        // --- PHASE 3: Avoidance Calculation ---
        var avoidanceJob = new AvoidanceJob
        {
            FineMap = fineMap,
            CoarseMap = coarseMap,
            PlanetCenter = planetData.Center,
            Config = config
        };
        state.Dependency = avoidanceJob.ScheduleParallel(_activeEnemyQuery, state.Dependency);

        // Dispose of the maps after the avoidance job completes
        state.Dependency = fineMap.Dispose(state.Dependency);
        state.Dependency = coarseMap.Dispose(state.Dependency);
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
        public float Mass;
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
            if (!TransformLookup.HasComponent(PlayerEntity))
                return;

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
        public NativeParallelMultiHashMap<int, AvoidanceData>.ParallelWriter FineMap;
        public NativeParallelMultiHashMap<int, AvoidanceData>.ParallelWriter CoarseMap;
        public AvoidanceConfig Config;

        public void Execute(Entity entity, in LocalTransform transform, in Avoidance avoidance)
        {
            float cellSize = Config.CellSizeFor(avoidance.Radius);
            var data = new AvoidanceData
            {
                Position = transform.Position,
                Entity = entity,
                Radius = avoidance.Radius,
                Mass = avoidance.Mass
            };

            var hash = (int)math.hash((int3)math.floor(transform.Position / cellSize));
            if (Config.IsCoarse(avoidance.Radius))
                CoarseMap.Add(hash, data);
            else
                FineMap.Add(hash, data);
        }
    }

    [BurstCompile]
    private partial struct PopulateObstacleSpatialMapJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, AvoidanceData>.ParallelWriter CoarseMap;
        public float CoarseCellSize;
        public float ObstacleMass;

        public void Execute(Entity entity, in LocalTransform transform, in Obstacle obstacle)
        {
            var hash = (int)math.hash((int3)math.floor(transform.Position / CoarseCellSize));
            CoarseMap.Add(hash, new AvoidanceData
            {
                Position = transform.Position,
                Entity = entity,
                Radius = obstacle.AvoidanceRadius,
                Mass = ObstacleMass
            });
        }
    }

    [BurstCompile]
    private partial struct AvoidanceJob : IJobEntity
    {
        /// <summary>
        /// Sums repulsion from neighbours and projects the result onto the planet's tangent plane.
        /// Small/Medium query both grids (peers in the fine grid, big entities + obstacles in the coarse
        /// one); Big/Giant query only the coarse grid — they ignore the trash they are meant to shove.
        /// </summary>
        [ReadOnly] public NativeParallelMultiHashMap<int, AvoidanceData> FineMap;
        [ReadOnly] public NativeParallelMultiHashMap<int, AvoidanceData> CoarseMap;
        public float3 PlanetCenter;
        public AvoidanceConfig Config;

        public void Execute(Entity entity, in Avoidance avoidance, in LocalTransform transform, ref SteeringForce steering)
        {
            float selfRadius = avoidance.Radius;
            float selfMass = avoidance.Mass;
            bool selfCoarse = Config.IsCoarse(selfRadius);

            // Everyone avoids the large entities and obstacles in the coarse grid.
            float3 avoidanceForce = Accumulate(in CoarseMap, Config.CoarseCellSize, entity,
                transform.Position, selfRadius, selfMass);

            // Only the small (fine-grid) entities also keep spacing among themselves.
            if (!selfCoarse)
                avoidanceForce += Accumulate(in FineMap, Config.FineCellSize, entity,
                    transform.Position, selfRadius, selfMass);

            // Constrain the avoidance force to the surface of the planet (tangent plane)
            float3 surfaceNormal = math.normalize(transform.Position - PlanetCenter);
            avoidanceForce -= surfaceNormal * math.dot(avoidanceForce, surfaceNormal);

            float forceMagnitude = math.length(avoidanceForce);
            if (forceMagnitude > Config.MaxSteeringForce)
                avoidanceForce = math.normalize(avoidanceForce) * Config.MaxSteeringForce;

            steering.Value = avoidanceForce;
        }

        /// <summary>
        /// Sums the repulsion contributed by one spatial map, scanning the 3x3x3 cell neighbourhood
        /// around <paramref name="position"/>.
        /// The Y axis of that neighbourhood is NOT redundant: entities live on a sphere, so a local
        /// surface patch is only axis-aligned near the poles — around the equator the surface is
        /// vertical in world space and neighbours are spread along Y.
        /// Force is split by mass ratio: what I receive scales with the OTHER's share of our combined
        /// mass, so a heavy neighbour pushes me fully while I barely move it.
        /// </summary>
        private float3 Accumulate(
            in NativeParallelMultiHashMap<int, AvoidanceData> map,
            float cellSize,
            Entity self,
            float3 position,
            float selfRadius,
            float selfMass)
        {
            float3 force = float3.zero;
            int3 centerCell = (int3)math.floor(position / cellSize);

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        int3 neighborCell = centerCell + new int3(x, y, z);
                        int hash = (int)math.hash(neighborCell);

                        if (!map.TryGetFirstValue(hash, out AvoidanceData other, out var it))
                            continue;

                        do
                        {
                            if (other.Entity == self)
                                continue;

                            float3 toSelf = position - other.Position;
                            float distSq = math.lengthsq(toSelf);

                            float combinedRadius = selfRadius + other.Radius;

                            if (distSq < combinedRadius * combinedRadius)
                            {
                                // Share of the collision I absorb, by mass. other/(self+other): a much
                                // heavier neighbour -> ~1 (I take the full push); an obstacle -> ~1;
                                // equal mass -> 0.5 (symmetric, as before).
                                float massRatio = other.Mass / (selfMass + other.Mass);

                                if (distSq > 0.001f)
                                {
                                    float dist = math.sqrt(distSq);
                                    float penForce = combinedRadius - dist;
                                    float3 pushDir = toSelf / dist;

                                    force += pushDir * (penForce * massRatio * Config.ForceGain);
                                }
                                else
                                {
                                    // Exact overlap: pick a deterministic ANTISYMMETRIC direction so the
                                    // two entities split apart instead of drifting the same way (a
                                    // symmetric seed would send both the same direction).
                                    var rnd = Unity.Mathematics.Random.CreateFromIndex(
                                        (uint)math.min(self.Index, other.Entity.Index) ^
                                        (uint)math.max(self.Index, other.Entity.Index));
                                    float3 dir = rnd.NextFloat3Direction();
                                    force += (self.Index < other.Entity.Index ? dir : -dir)
                                             * (massRatio * Config.ForceGain);
                                }
                            }
                        } while (map.TryGetNextValue(out other, ref it));
                    }
                }
            }

            return force;
        }
    }
}
