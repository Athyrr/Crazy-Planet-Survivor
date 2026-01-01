using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Burst;

/// <summary>
/// System that handle enemy spell to be casted by sending a CastSpellRequest. NOT THE SPELLS THEMSELVES. It processes enemies who have spells ready to be used.
/// <para>
/// It calculates the distance to the player by following the surface of the planet.
/// If the player is within range, the system creates a new entity with a `CastSpellRequest` component and empties the `EnemySpellReady` buffer to wait for the next cooldown cycle.
/// </para>
/// </summary>
[BurstCompile]
public partial struct EnemyTargetingSystem : ISystem
{
    // Lookups to access data safely inside jobs
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<PlanetData> _planetLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Enemy>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<PlanetData>();

        _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
        _planetLookup = state.GetComponentLookup<PlanetData>(isReadOnly: true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // 1. Update Lookups (Must be done every frame)
        _transformLookup.Update(ref state);
        _planetLookup.Update(ref state);

        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState)) return;
        if (gameState.State != EGameState.Running) return;

        // 2. Get Entity Handles Only (Fast, no sync required)
        var playerEntity = SystemAPI.GetSingletonEntity<Player>();
        var planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();
        var spellDatabase = SystemAPI.GetSingleton<SpellsDatabase>();

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();

        // 3. Schedule Job (Non-blocking)
        var job = new EnemyTargetingJob
        {
            SpellDatabaseRef = spellDatabase.Blobs,
            PlayerEntity = playerEntity,
            PlanetEntity = planetEntity,
            TransformLookup = _transformLookup,
            PlanetLookup = _planetLookup,
            ECB = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter()
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(Stats), typeof(Enemy))]
    private partial struct EnemyTargetingJob : IJobEntity
    {
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

        public Entity PlayerEntity;
        public Entity PlanetEntity;

        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<PlanetData> PlanetLookup;

        public EntityCommandBuffer.ParallelWriter ECB;

        void Execute([ChunkIndexInQuery] int chunkIndex, ref DynamicBuffer<EnemySpellReady> readySpells, in LocalTransform transform, in Entity entity)
        {
            if (readySpells.IsEmpty) return;

            // Safety check
            if (!TransformLookup.HasComponent(PlayerEntity) || !PlanetLookup.HasComponent(PlanetEntity)) return;

            // Gather global data once
            float3 playerPos = TransformLookup[PlayerEntity].Position;
            float planetRadius = PlanetLookup[PlanetEntity].Radius;

            // OPTIMIZATION 1: Pre-calculate the "Max Possible Distance" (Half Circumference)
            float maxSurfaceDist = math.PI * planetRadius;

            // OPTIMIZATION 2: Calculate Squared Straight-Line Distance to player once
            // This is much faster than calculating surface distance
            float distToPlayerSq = math.distancesq(transform.Position, playerPos);

            for (int i = 0; i < readySpells.Length; i++)
            {
                var spell = readySpells[i].Spell;
                ref readonly var spellData = ref SpellDatabaseRef.Value.Spells[spell.DatabaseIndex];

                bool isInRange = false;

                // CHECK 1: Is the spell "Global"?
                // If range >= max surface distance, we hit ANYWHERE. No math needed.
                if (spellData.BaseCastRange >= maxSurfaceDist)
                {
                    isInRange = true;
                }
                else
                {
                    // CHECK 2: Optimized "Chord" Math
                    // Instead of calculating the Arc (expensive), we convert the Range (Arc)
                    // into a required Straight-Line Distance (Chord).
                    // Chord = 2*R * sin(Arc / 2R)

                    // Note: If you have many spells, you should cache 'ThresholdChordSq' in the BlobData
                    // to avoid doing this sin() calculation here.
                    float thresholdChord = 2.0f * planetRadius * math.sin(spellData.BaseCastRange / (2.0f * planetRadius));

                    if (distToPlayerSq <= thresholdChord * thresholdChord)
                    {
                        isInRange = true;
                    }
                }

                if (isInRange)
                {
                    var request = ECB.CreateEntity(chunkIndex);
                    ECB.AddComponent(chunkIndex, request, new CastSpellRequest
                    {
                        Caster = entity,
                        Target = PlayerEntity,
                        DatabaseIndex = spell.DatabaseIndex
                    });
                }
            }

            readySpells.Clear();
        }
    }
}