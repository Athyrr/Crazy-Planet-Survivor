using Unity.Entities;

/// <summary>
/// System that handles spell cooldown and sends request to cast spells in the corresponding systems.
/// </summary>
public partial struct SpellCasterSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<Stats>();
        state.RequireForUpdate<ActiveSpell>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach (var (stats, entity) in
            SystemAPI.Query<RefRO<Stats>>().WithEntityAccess())
        {
            // Get editable spell buffer 
            DynamicBuffer<ActiveSpell> spells = SystemAPI.GetBuffer<ActiveSpell>(entity);

            if (!spells.IsCreated || spells.IsEmpty)
                continue;

            // Iterate through all spells of an entity
            for (int i = 0; i < spells.Length; i++)
            {
                ActiveSpell spell = spells[i];

                if (spell.CooldownTimer < 0)
                    spell.CooldownTimer = spell.BaseCooldown * (1 - stats.ValueRO.CooldownReduction);

                spell.CooldownTimer -= deltaTime;

                if (spell.CooldownTimer <= 0)
                {
                    // Create request + set datas
                    var request = ecb.CreateEntity();
                    ecb.AddComponent(request, new CastSpellRequest()
                    {
                        Caster = entity,
                        SpellID = spell.ID,
                    });

                    float cooldown = spell.BaseCooldown * (1 - stats.ValueRO.CooldownReduction);
                    spell.CooldownTimer = cooldown;
                }

                spells[i] = spell;
            }
        }

        // Execte command buffer
        ecb.Playback(state.EntityManager);
    }
}
