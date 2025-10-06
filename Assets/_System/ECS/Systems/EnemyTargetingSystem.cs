using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

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
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Enemy>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<PlanetData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<Player>(out Entity playerEntity))
            return;

        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out Entity planet))
            return;

        var spellDatabase = SystemAPI.GetSingleton<SpellsDatabase>();

        var planetData = state.EntityManager.GetComponentData<PlanetData>(planet);
        var planetTransform = SystemAPI.GetComponent<LocalTransform>(planet);
        var playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        EntityCommandBuffer ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var job = new EnemyTargetingJob
        {
            SpellDatabaseRef = spellDatabase.Blobs,
            Player = playerEntity,
            PlayerPosition = playerTransform.Position,
            PlanetPosition = planetTransform.Position,
            PlanetRadius = planetData.Radius,
            ECB = ecb.AsParallelWriter()
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }


    /// <summary>
    /// Job that processes enemies with ready spells, checks if the player is within a ready spell range, and sends a CastSpellRequest if so.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(Stats), typeof(Enemy))]
    private partial struct EnemyTargetingJob : IJobEntity
    {
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

        public Entity Player;
        public float3 PlayerPosition;
        public float3 PlanetPosition;
        public float PlanetRadius;
        public EntityCommandBuffer.ParallelWriter ECB;

        void Execute([ChunkIndexInQuery] int chunkIndex,
            ref DynamicBuffer<EnemySpellReady> readySpells,
            in LocalTransform transform,
            in Entity entity)
        {

            for (int i = 0; i < readySpells.Length; i++)
            {
                var spell = readySpells[i].Spell;
                ref readonly var spellData = ref SpellDatabaseRef.Value.Spells[spell.DatabaseIndex];

                PlanetMovementUtils.GetSurfaceDistanceBetweenPoints(in transform.Position, in PlayerPosition, PlanetPosition, PlanetRadius, out float distance);

                if (distance <= spellData.BaseRange)
                {
                    var request = ECB.CreateEntity(chunkIndex);
                    ECB.AddComponent(chunkIndex, request, new CastSpellRequest
                    {
                        Caster = entity,
                        Target = Player,
                        //DatabaseRef = spellToCast.DatabaseRef,
                        DatabaseIndex = spell.DatabaseIndex
                    });

                    switch (spellData.ID)
                    {
                        case ESpellID.Fireball:
                            ECB.AddComponent<FireballRequestTag>(chunkIndex, request);
                            break;

                        case ESpellID.LightningStrike:
                            ECB.AddComponent<LightningStrikeRequestTag>(chunkIndex, request);
                            break;
                    }
                }
            }

            readySpells.Clear();
        }
    }
}
