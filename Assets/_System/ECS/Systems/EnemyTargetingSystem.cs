using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Burst;

/// <summary>
/// Evaluates enemies with ready spells and issues <see cref="CastSpellRequest"/> entities if the player is within range.
/// This system calculates distances along the surface of a spherical planet using optimized chord-length math.
/// </summary>
[BurstCompile]
public partial struct EnemyTargetingSystem : ISystem
{
    /// <summary> Lookup for transform data of entities outside the current job's scope. </summary>
    private ComponentLookup<LocalTransform> _transformLookup;
    /// <summary> Lookup for planet-specific data (like radius). </summary>
    private ComponentLookup<PlanetData> _planetLookup;

    [BurstCompile]
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
        // Refresh lookups to ensure the job has access to the latest frame data
        _transformLookup.Update(ref state);
        _planetLookup.Update(ref state);

        // Only process targeting if the game is actively running
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState)) return;
        if (gameState.State != EGameState.Running) return;

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();
        var planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();
        var spellDatabase = SystemAPI.GetSingleton<SpellsDatabase>();

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();

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

    /// <summary>
    /// Processes each enemy with ready spells, checking if the player is within the spell's cast range.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(Stats), typeof(Enemy))]
    private partial struct EnemyTargetingJob : IJobEntity
    {
        /// <summary> Reference to the blob asset containing all spell configuration data. </summary>
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

        public Entity PlayerEntity;
        public Entity PlanetEntity;

        /// <summary> Read-only lookup for transforms (Player/Planet). </summary>
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        /// <summary> Read-only lookup for planet data. </summary>
        [ReadOnly] public ComponentLookup<PlanetData> PlanetLookup;

        /// <summary> Command buffer to create spell request entities. </summary>
        public EntityCommandBuffer.ParallelWriter ECB;

        void Execute([ChunkIndexInQuery] int chunkIndex, ref DynamicBuffer<EnemySpellReady> readySpells, in LocalTransform transform, in Entity entity)
        {
            if (readySpells.IsEmpty) return;

            // Ensure required global entities still exist
            if (!TransformLookup.HasComponent(PlayerEntity) || !PlanetLookup.HasComponent(PlanetEntity)) return;

            float3 playerPos = TransformLookup[PlayerEntity].Position;
            float planetRadius = PlanetLookup[PlanetEntity].Radius;

            // Pre-calculate the maximum possible surface distance (half circumference)
            float maxSurfaceDist = math.PI * planetRadius;

            // Calculate the squared straight-line (Euclidean) distance to the player.
            // This is used for comparison against the calculated chord threshold.
            float distToPlayerSq = math.distancesq(transform.Position, playerPos);

            for (int i = 0; i < readySpells.Length; i++)
            {
                var spell = readySpells[i].Spell;
                ref readonly var spellData = ref SpellDatabaseRef.Value.Spells[spell.DatabaseIndex];
                bool isInRange = false;

                if (spellData.BaseCastRange >= maxSurfaceDist)
                {
                    // If range covers half the planet, it's effectively global
                    isInRange = true;
                }
                else
                {
                    // Optimized Range Check:
                    // Instead of calculating the Arc distance (expensive), we convert the spell's 
                    // Range (Arc) into a Straight-Line Distance (Chord).
                    // Formula: Chord = 2 * R * sin(Arc / 2R)
                    float thresholdChord = 2.0f * planetRadius * math.sin(spellData.BaseCastRange / (2.0f * planetRadius));

                    if (distToPlayerSq <= thresholdChord * thresholdChord)
                    {
                        isInRange = true;
                    }
                }

                if (isInRange)
                {
                    // Create a request entity to be processed by the SpellSystem
                    var request = ECB.CreateEntity(chunkIndex);
                    ECB.AddComponent(chunkIndex, request, new CastSpellRequest
                    {
                        Caster = entity,
                        Target = PlayerEntity,
                        DatabaseIndex = spell.DatabaseIndex
                    });
                }
            }

            // Clear the buffer so spells aren't re-cast until the cooldown system refills it
            readySpells.Clear();
        }
    }
}