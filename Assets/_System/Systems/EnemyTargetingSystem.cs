using Unity.Burst;
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

        //@todo check every spell ready buffer + for each spell, check range and send CastSpellRequest with spell data
        //@todo Job chunk or parallel for

        if (!SystemAPI.TryGetSingletonEntity<Player>(out Entity playerEntity))
            return;

        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out Entity planet))
            return;

        var planetData = state.EntityManager.GetComponentData<PlanetData>(planet);
        var planetTransform = SystemAPI.GetComponent<LocalTransform>(planet);
        var playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        EntityCommandBuffer ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

       var job = new EnemyTargetingJob
        {
            PlayerPosition = playerTransform.Position,
            PlanetPosition = planetTransform.Position,
            PlanetRadius = planetData.Radius,
            ECB = ecb.AsParallelWriter()
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
        state.Dependency.Complete();
    }


    [WithAll(typeof(Stats), typeof(Enemy))]
    [BurstCompile]
    private partial struct EnemyTargetingJob : IJobEntity
    {
        public float3 PlayerPosition;
        public float3 PlanetPosition;
        public float PlanetRadius;
        public EntityCommandBuffer.ParallelWriter ECB;

        void Execute([ChunkIndexInQuery] int index,
            ref DynamicBuffer<EnemySpellReady> readySpells,
            in LocalTransform transform,
            in Entity entity)
        {

            for (int i = 0; i < readySpells.Length; i++)
            {
                var spellToCast = readySpells[i].Spell;

                PlanetMovementUtils.GetSurfaceDistanceBetweenPoints(in transform.Position, in PlayerPosition, PlanetPosition, PlanetRadius, out float distance);
                float distanceSquared = distance * distance;
                if (distanceSquared <= spellToCast.Range * spellToCast.Range)
                {
                    var request = ECB.CreateEntity(index);
                    ECB.AddComponent(index, request, new CastSpellRequest
                    {
                        Caster = entity,
                        SpellID = spellToCast.ID
                    });
                }
            }

            readySpells.Clear();
        }
    }
}
