using Unity.Entities;
using Unity.Transforms;

public partial struct EnemyTargetingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Enemy>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<PlanetData>();
    }

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

        foreach (var (transform, spells, spellReadyBuffer, entity) in SystemAPI.Query<RefRO<LocalTransform>, DynamicBuffer<ActiveSpell>, DynamicBuffer<EnemySpellReady>>().WithAll<Enemy>().WithEntityAccess())
        {
            foreach (EnemySpellReady spellReady in spellReadyBuffer)
            {

                var spellToCast = spellReady.Spell;

                PlanetMovementUtils.GetSurfaceDistanceBetweenPoints(in transform.ValueRO.Position, in playerTransform.Position, planetTransform.Position, planetData.Radius, out float distance);
                float distanceSquared = distance * distance;
                if (distanceSquared <= spellToCast.Range * spellToCast.Range)
                {
                    var request = ecb.CreateEntity();
                    ecb.AddComponent(request, new CastSpellRequest
                    {
                        Caster = entity,
                        SpellID = spellToCast.ID
                    });
                }
            }

            spellReadyBuffer.Clear();
        }
    }
}
