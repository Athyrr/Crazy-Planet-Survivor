using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>Sums this frame's SpellDamageEvent queue per spell DB index and adds it to the player's
/// ActiveSpell.TotalDamageDealt. Was duplicated verbatim in CollisionSystem and AreaAttackSystem — this is
/// the single shared copy both reference.</summary>
[BurstCompile]
public struct TrackDamageJob : IJob
{
    public NativeQueue<SpellDamageEvent> DamageEventsQueue;
    public BufferLookup<ActiveSpell> ActiveSpellLookup;
    public Entity PlayerEntity;

    public void Execute()
    {
        var sums = new NativeHashMap<int, int>(16, Allocator.Temp);
        while (DamageEventsQueue.TryDequeue(out var evt))
        {
            if (sums.ContainsKey(evt.DatabaseIndex))
                sums[evt.DatabaseIndex] += evt.DamageAmount;
            else
                sums.Add(evt.DatabaseIndex, evt.DamageAmount);
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
