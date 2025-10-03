using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[BurstCompile]
public partial struct SpellInitializationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<BaseSpell>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<SpellsDatabase>(out var database))
            return;

        Debug.Log("BDD Found!");

        ref var spellBlobs = ref database.Blobs.Value.Spells;

        var blobMap = new NativeHashMap<SpellKey, int>(spellBlobs.Length, Allocator.Temp);
        for (int i = 0; i < spellBlobs.Length; i++)
        {
            blobMap.TryAdd(new SpellKey { Value = spellBlobs[i].ID }, i);
        }

        var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach (var (baseSpellBuffer, entity) in
                 SystemAPI.Query<DynamicBuffer<BaseSpell>>().WithEntityAccess())
        {
            var activeSpellBuffer = SystemAPI.GetBuffer<ActiveSpell>(entity);

            foreach (var spellToInit in baseSpellBuffer)
            {
                if (blobMap.TryGetValue(new SpellKey { Value = spellToInit.ID }, out var blobIndex))
                {
                    activeSpellBuffer.Add(new ActiveSpell
                    {
                        DatabaseRef = database.Blobs,
                        DatabaseIndex = blobIndex,
                        Level = 1,
                        CooldownTimer = spellBlobs[blobIndex].BaseCooldown
                    });
                }
            }

            ecb.RemoveComponent<BaseSpell>(entity);
        }

        ecb.Playback(state.EntityManager);
        blobMap.Dispose();

        state.Enabled = false;
    }
}
