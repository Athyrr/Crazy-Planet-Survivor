using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>Sums this frame's SpellDamageEvent queue per spell DB index and adds it to the player's
/// ActiveSpell.TotalDamageDealt. The player's own hits are credited to the spell that dealt them; a Summon's hits to its
/// owner's Summon spell (Summon.SourceDatabaseIndex); any other caster (enemies, dead casters) is not credited
/// (spec allegeance-summon §3). Shared by CollisionSystem, AreaAttackSystem and ActiveEffectsSystem.</summary>
[BurstCompile]
public struct TrackDamageJob : IJob
{
    public NativeQueue<SpellDamageEvent> DamageEventsQueue;
    public BufferLookup<ActiveSpell> ActiveSpellLookup;
    [ReadOnly] public ComponentLookup<Summon> SummonLookup;
    public Entity PlayerEntity;

    public void Execute()
    {
        var sums = new NativeHashMap<int, int>(16, Allocator.Temp);
        while (DamageEventsQueue.TryDequeue(out var evt))
        {
            int creditIndex;
            if (evt.Caster == PlayerEntity)
                creditIndex = evt.DatabaseIndex;
            else if (SummonLookup.TryGetComponent(evt.Caster, out var summon) && summon.Owner == PlayerEntity)
                creditIndex = summon.SourceDatabaseIndex;
            else
                continue;

            if (sums.ContainsKey(creditIndex))
                sums[creditIndex] += evt.DamageAmount;
            else
                sums.Add(creditIndex, evt.DamageAmount);
        }

        if (PlayerEntity != Entity.Null && ActiveSpellLookup.TryGetBuffer(PlayerEntity, out var buffer))
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                var spell = buffer[i];
                if (sums.TryGetValue(spell.DatabaseIndex, out int totalAdded))
                {
                    spell.TotalDamageDealt += totalAdded;
                    buffer[i] = spell;
                }
            }
        }
        sums.Dispose();
    }
}
