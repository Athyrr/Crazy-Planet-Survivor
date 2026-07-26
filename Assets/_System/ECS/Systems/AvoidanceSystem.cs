using _System.Settings;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Burst;
using Unity.Jobs;

/// <summary>
/// Local avoidance via spatial hashing + a visibility LOD, keeping the cost near O(N) instead of O(N²).
/// Two grids keyed by per-entity avoidance radius: a fine grid for the numerous small-radius entities,
/// a coarse grid for the few large-radius ones plus static obstacles, so small entities never scan
/// obstacle-sized cells. Repulsion is split by mass ratio: a heavy entity (or obstacle) shoves the
/// horde aside without being shoved back. Writes a per-entity SteeringForce consumed by the movement system.
/// </summary>
[UpdateInGroup(typeof(CustomUpdateGroup))]
public partial struct AvoidanceSystem : ISystem
{
    private EntityQuery _activeEnemyQuery;
    private EntityQuery _allEnemyQuery;
    private EntityQuery _obstaclesQuery;
    private ComponentLookup<LocalTransform> _transformLookup;

    private float _timeSinceLastLOD;

    // Persistent spatial maps, cleared and refilled each tick. Sized to the hard enemy cap so no
    // per-frame entity count is needed to allocate them.
    private NativeParallelMultiHashMap<int, AvoidanceData> _fineMap;
    private NativeParallelMultiHashMap<int, AvoidanceData> _coarseMap;
    private bool _mapsAllocated;
    private int _fineCapacity;
    private int _coarseCapacity;

    // Last tick's populate+avoidance jobs, which read/write the persistent maps. They must be complete
    // before this tick clears and refills the maps on the main thread — otherwise clearing races them.
    private JobHandle _mapJobHandle;

    /// <summary> Enemy cap used to size the maps when no SpawnerSettings singleton exists yet. </summary>
    private const int FallbackMaxEnemies = 500;

    /// <summary> Frequency of the Level of Detail (LOD) distance check. </summary>
    private const float LodCheckInterval = 0.5f;

    /// <summary> Horizon cosines used only when no CpAvoidanceSettings asset exists (~Earth R50, h35). </summary>
    private const float FallbackLodCosDegrade = 0.55f;
    private const float FallbackLodCosRestore = 0.58f;
    /// <summary> Camera height fallback when neither the camera nor the avoidance settings asset exists. </summary>
    private const float FallbackCameraHeight = 35f;

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

    public void OnDestroy(ref SystemState state)
    {
        if (_mapsAllocated)
        {
            _mapJobHandle.Complete();
            _fineMap.Dispose();
            _coarseMap.Dispose();
            _mapsAllocated = false;
        }
    }

    /// <summary>
    /// Ensures the persistent maps exist and are large enough for the given upper bounds, reallocating
    /// only when they need to grow (in practice once: MaxEnemies is baked and obstacle counts are stable).
    /// </summary>
    private void EnsureCapacity(int fineNeeded, int coarseNeeded)
    {
        if (_mapsAllocated && _fineCapacity >= fineNeeded && _coarseCapacity >= coarseNeeded)
            return;

        if (_mapsAllocated)
        {
            _fineMap.Dispose();
            _coarseMap.Dispose();
        }

        _fineCapacity = math.max(fineNeeded, 1);
        _coarseCapacity = math.max(coarseNeeded, 1);
        _fineMap = new NativeParallelMultiHashMap<int, AvoidanceData>(_fineCapacity, Allocator.Persistent);
        _coarseMap = new NativeParallelMultiHashMap<int, AvoidanceData>(_coarseCapacity, Allocator.Persistent);
        _mapsAllocated = true;
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

        // --- PHASE 1: Level of Detail (LOD) ---
        // Toggle each entity's Avoidance on/off by whether it is within the camera horizon of the player
        // (angular, derived from the per-planet camera height). Off = hidden by curvature; the movement
        // system also switches to a raycast-free snap for those, gated on the same enabled flag.
        _timeSinceLastLOD += SystemAPI.Time.DeltaTime;
        if (_timeSinceLastLOD > LodCheckInterval)
        {
            _timeSinceLastLOD = 0;
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();

            // Camera height above the surface for this planet (falls back to the settings default).
            float cameraHeight = CpAvoidanceSettings.I != null
                ? CpAvoidanceSettings.I.LodFallbackCameraHeight
                : FallbackCameraHeight;
            if (CpBaseCameraSettings.I != null)
                cameraHeight = CpBaseCameraSettings.PlanetCameraSettings[planetData.PlanetID].RadiusOffset;

            float cosDegrade = FallbackLodCosDegrade;
            float cosRestore = FallbackLodCosRestore;
            if (CpAvoidanceSettings.I != null)
                CpAvoidanceSettings.I.ComputeHorizonThresholds(planetData.Radius, cameraHeight,
                    out cosDegrade, out cosRestore);

            var lodJob = new LodJob
            {
                TransformLookup = _transformLookup,
                PlayerEntity = playerEntity,
                PlanetCenter = planetData.Center,
                CosDegrade = cosDegrade,
                CosRestore = cosRestore,
                Ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter()
            };
            state.Dependency = lodJob.ScheduleParallel(_allEnemyQuery, state.Dependency);
        }

        // --- PHASE 2: Spatial Hashing ---
        // Cheap structural early-out (no enabled-bit scan): nothing to do without any avoidance entity.
        if (_allEnemyQuery.IsEmptyIgnoreFilter)
            return;

        // Maps are sized to the hard caps (fine = MaxEnemies, coarse = MaxEnemies + obstacles), so the
        // live enemy count — which would sync on the enableable query — is never needed.
        int maxEnemies = SystemAPI.TryGetSingleton<SpawnerSettings>(out var spawnerSettings)
            ? spawnerSettings.MaxEnemies
            : FallbackMaxEnemies;
        int obstacleCount = _obstaclesQuery.CalculateEntityCount();

        // Persistent maps: last tick's jobs must finish before we clear/refill them on the main thread.
        _mapJobHandle.Complete();
        EnsureCapacity(maxEnemies, maxEnemies + obstacleCount);

        _fineMap.Clear();
        _coarseMap.Clear();

        // Enemies route themselves to the fine or coarse map by radius.
        var populateEnemiesHandle = new PopulateEnemiesSpatialMapJob
        {
            FineMap = _fineMap.AsParallelWriter(),
            CoarseMap = _coarseMap.AsParallelWriter(),
            Config = config
        }.ScheduleParallel(_activeEnemyQuery, state.Dependency);

        // Obstacles also live in the coarse map, so this must run AFTER the enemy populate (both write
        // the coarse map's ParallelWriter — concurrent writes to one writer would race).
        var populateObstaclesHandle = new PopulateObstacleSpatialMapJob
        {
            CoarseMap = _coarseMap.AsParallelWriter(),
            CoarseCellSize = config.CoarseCellSize,
            ObstacleMass = config.ObstacleMass
        }.ScheduleParallel(_obstaclesQuery, populateEnemiesHandle);

        state.Dependency = populateObstaclesHandle;

        // --- PHASE 3: Avoidance Calculation ---
        var avoidanceJob = new AvoidanceJob
        {
            FineMap = _fineMap,
            CoarseMap = _coarseMap,
            PlanetCenter = planetData.Center,
            Config = config
        };
        state.Dependency = avoidanceJob.ScheduleParallel(_activeEnemyQuery, state.Dependency);

        // Remember the map-touching jobs so next tick can complete them before clearing the maps.
        // Maps are persistent: no per-frame dispose. They are freed in OnDestroy.
        _mapJobHandle = state.Dependency;
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
        /// Toggles the Avoidance component's enabled state by angular distance to the player: the entity
        /// is kept active while its surface normal is within the camera horizon of the player's, and
        /// dropped past it. Hysteresis (CosDegrade &lt; CosRestore) keeps entities at the edge from
        /// chattering between the two states.
        /// </summary>
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        public Entity PlayerEntity;
        public float3 PlanetCenter;
        public float CosDegrade;
        public float CosRestore;
        public EntityCommandBuffer.ParallelWriter Ecb;

        public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex, EnabledRefRO<Avoidance> avoidanceEnabled, in LocalTransform transform)
        {
            if (!TransformLookup.HasComponent(PlayerEntity))
                return;

            float3 nPlayer = math.normalize(TransformLookup[PlayerEntity].Position - PlanetCenter);
            float3 nEnemy = math.normalize(transform.Position - PlanetCenter);
            float d = math.dot(nEnemy, nPlayer);

            bool wasActive = avoidanceEnabled.ValueRO;
            // Active entities stay active until they fall past the (farther) degrade boundary; inactive
            // ones stay inactive until they come back inside the (nearer) restore boundary.
            bool shouldBeActive = wasActive ? d > CosDegrade : d > CosRestore;

            if (wasActive != shouldBeActive)
                Ecb.SetComponentEnabled<Avoidance>(chunkIndex, entity, shouldBeActive);
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
