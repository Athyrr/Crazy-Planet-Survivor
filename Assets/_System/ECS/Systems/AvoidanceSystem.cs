using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

//[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateInGroup(typeof(TestUpdateGroup))]
//[UpdateBefore(typeof(EntitiesMovementSystem))]
[BurstCompile]
public partial struct AvoidanceSystem : ISystem
{
    private EntityQuery _activeEnemyQuery;
    private EntityQuery _allEnemyQuery;
    private float _timeSinceLastLOD;

    // Configuration
    private const float CellSize = 5.0f;
    private const float LodCheckInterval = 0.5f;
    private const float ActivationDistSq = 60f * 60f;


    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        // CORRECTION 1 : Utiliser EntityQueryBuilder (struct) compatible Burst
        // au lieu de EntityQueryDesc (class) qui plante Burst.
        var builder = new EntityQueryBuilder(Allocator.Temp);

        // 1. Requête pour les entités actives (Avoidance activé)
        builder.WithAll<Avoidance, LocalTransform>()
              .WithAllRW<SteeringForce>();
        _activeEnemyQuery = state.GetEntityQuery(builder);

        // 2. Requête pour le LOD (tout le monde, même désactivé)
        builder.Reset();
        builder.WithAll<Avoidance>()
              .WithAll<LocalTransform>()
              .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState); // Crucial pour le LOD
        _allEnemyQuery = state.GetEntityQuery(builder);

        builder.Dispose();
    }


    public void OnUpdate(ref SystemState state)
    {
        // Récupération des Singletons (Main Thread)
        var playerEntity = SystemAPI.GetSingletonEntity<Player>();
        var playerPos = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;

        var planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();
        var planetPos = SystemAPI.GetComponent<LocalTransform>(planetEntity).Position;

        // --- PHASE 1 : LOD (Rare) ---
        _timeSinceLastLOD += SystemAPI.Time.DeltaTime;
        if (_timeSinceLastLOD > LodCheckInterval)
        {
            _timeSinceLastLOD = 0;
            
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            var lodJob = new LodJob
            {
                PlayerPos = playerPos,
                DistSqThreshold = ActivationDistSq,
                Ecb = ecb
            };
            // CORRECTION 3 : Bien chainer la dépendance
            state.Dependency = lodJob.ScheduleParallel(_allEnemyQuery, state.Dependency);
        }

        // --- PHASE 2 : Spatial Hashing ---
        int activeCount = _activeEnemyQuery.CalculateEntityCount();
        if (activeCount == 0) return;

        // La map doit être dispose à la fin ducoup boum badabim on utilise Allocator.TempJob
        var spatialMap = new NativeParallelMultiHashMap<int, AvoidanceData>(activeCount, Allocator.TempJob);

        var populateJob = new PopulateSpatialMapJob
        {
            Map = spatialMap.AsParallelWriter(),
            CellSize = CellSize
        };
        state.Dependency = populateJob.ScheduleParallel(_activeEnemyQuery, state.Dependency);

        // --- PHASE 3 : Calcul d'Évitement ---
        var avoidanceJob = new AvoidanceJob
        {
            Map = spatialMap, // On passe la map en lecture
            CellSize = CellSize,
            PlanetCenter = planetPos
        };
        state.Dependency = avoidanceJob.ScheduleParallel(_activeEnemyQuery, state.Dependency);

        // Nettoyage de la mémoire après que les jobs soient finis (merci brogpt)
        state.Dependency = spatialMap.Dispose(state.Dependency);
    }

    // Structures internes
    private struct AvoidanceData
    {
        public float3 Position;
        public Entity Entity;
    }

    // --- JOBS ---

    [BurstCompile]
    private partial struct LodJob : IJobEntity
    {
        public float3 PlayerPos;
        public float DistSqThreshold;
        public EntityCommandBuffer.ParallelWriter Ecb;

        public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex, EnabledRefRO<Avoidance> avoidanceEnabled, in LocalTransform transform)
        {
            bool shouldBeActive = math.distancesq(transform.Position, PlayerPos) < DistSqThreshold;
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
        // CORRECTION 2 : est OBLIGATOIRE pour lire une Map dans un Job Parallèle
        [ReadOnly]
        public NativeParallelMultiHashMap<int, AvoidanceData> Map;
        public float CellSize;
        public float3 PlanetCenter;

        private const int MaxNeighboursToCheck = 10;

        public void Execute(Entity entity, in Avoidance avoidance, in LocalTransform transform, ref SteeringForce steering)
        {
            float3 avoidanceForce = float3.zero;
            int3 centerCell = (int3)math.floor(transform.Position / CellSize);
            int neighborsChecked = 0;

            // Parcours 3x3x3 peut etre optimisable mais bon j'ai vu 4 video qui faisaient comme ca so ca devrait le faire
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

                                if (distSq < avoidance.Radius * avoidance.Radius && distSq > 0.001f)
                                {
                                    avoidanceForce += (toSelf / distSq) * 4;
                                    neighborsChecked++;
                                    if (neighborsChecked >= MaxNeighboursToCheck)
                                        goto FinishedNeighbors;
                                }
                            } while (Map.TryGetNextValue(out other, ref it));
                        }
                    }
                }
            }

            FinishedNeighbors:
            // Projection
            float3 surfaceNormal = math.normalize(transform.Position - PlanetCenter);
            avoidanceForce = avoidanceForce - surfaceNormal * math.dot(avoidanceForce, surfaceNormal);

            steering.Value = avoidanceForce * avoidance.Weight;
        }
    }
}