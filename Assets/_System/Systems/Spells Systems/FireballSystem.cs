using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

public partial struct FireballSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Stats>();
        state.RequireForUpdate<ActiveSpell>();
        state.RequireForUpdate<CastSpellRequest>();

        // @todo get spell data from scriptable object ex damage, area, range, element...
    }

    public void OnUpdate(ref SystemState state)
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach (var (request, requestEntity) in
            SystemAPI.Query<RefRO<CastSpellRequest>>().WithEntityAccess())
        {
            if (request.ValueRO.SpellID == ESpellID.FireBall)
            {
                var casterPosition = SystemAPI.GetComponent<LocalTransform>(request.ValueRO.Caster).Position;
                Stats casterStats = SystemAPI.GetComponent<Stats>(request.ValueRO.Caster);

                float damage = 20 + casterStats.Damage;
                //Debug.Log($"Cast FIREBALL ! Damages: {damage}, caster: {request.ValueRO.Caster}");
                Debug.Log($"Cast FIREBALL ! Damages: {damage}");
                ecb.DestroyEntity(requestEntity);
            }
        }

        ecb.Playback(state.EntityManager);
    }

}
