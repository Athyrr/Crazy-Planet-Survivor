using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Burst;

/// <summary>
/// Evaluates enemies with ready spells and issues <see cref="CastSpellRequest"/> entities if the player is within range.
/// This system calculates distances along the surface of a spherical planet using optimized chord-length math.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PlayerSpawnerSystem))]
[BurstCompile]
public partial struct EnemyTargetingSystem : ISystem
{
    private ComponentLookup<LocalTransform> _transformLookup;
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
        _transformLookup.Update(ref state);
        _planetLookup.Update(ref state);

        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;
        if (gameState.State != EGameState.Running)
            return;

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
    [WithAll(typeof(CoreStats), typeof(Enemy))]
    private partial struct EnemyTargetingJob : IJobEntity
    {
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

        public Entity PlayerEntity;
        public Entity PlanetEntity;

        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<PlanetData> PlanetLookup;

        public EntityCommandBuffer.ParallelWriter ECB;

        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, ref DynamicBuffer<EnemySpellReady> readySpells, ref DynamicBuffer<ActiveSpell> activeSpells, in LocalTransform transform)
        {
            if (readySpells.IsEmpty) return;

            if (!TransformLookup.HasComponent(PlayerEntity) || !PlanetLookup.HasComponent(PlanetEntity))
                return;

            float3 playerPos = TransformLookup[PlayerEntity].Position;
            float planetRadius = PlanetLookup[PlanetEntity].Radius;

            float maxSurfaceDist = math.PI * planetRadius;

            float distToPlayerSq = math.distancesq(transform.Position, playerPos);

            for (int i = 0; i < readySpells.Length; i++)
            {
                var spell = readySpells[i].Spell;
                
                float finalRange = spell.FinalRange > 0 ? spell.FinalRange : 1f; 
                
                bool isInRange = false;

                if (finalRange >= maxSurfaceDist)
                {
                    isInRange = true;
                }
                else
                {
                    float thresholdChord = 2.0f * planetRadius * math.sin(finalRange / (2.0f * planetRadius));
                    if (distToPlayerSq <= thresholdChord * thresholdChord)
                    {
                        isInRange = true;
                    }
                }

                if (isInRange)
                {
                    // todo create request on caster entity instead of creating a new entity for the request
                    // Create a request entity to be processed by the SpellSystem
                    var request = ECB.CreateEntity(chunkIndex);
                    ECB.AddComponent(chunkIndex, request, new CastSpellRequest
                    {
                        Caster = entity,
                        Target = PlayerEntity,
                        DatabaseIndex = spell.DatabaseIndex
                    });
                    
                    // Reset the cooldown of the spell that was just cast
                    // todo optimize by storing the index of the spell in the ActiveSpells buffer in EnemySpellReady to avoid this loop
                    // todo reset casted spell cooldown or all ready spells cooldown? 
                    for (int j = 0; j < activeSpells.Length; j++)
                    {
                        if (activeSpells[j].DatabaseIndex == spell.DatabaseIndex)
                        {
                            var activeSpellToReset = activeSpells[j];
                            activeSpellToReset.CurrentCooldown = activeSpellToReset.FinalCooldown;
                            activeSpells[j] = activeSpellToReset; 
                            break;
                        }
                    }
                }
            }

            // Clear the buffer so spells aren't re-cast until the cooldown system refills it
            readySpells.Clear();
        }
    }
}