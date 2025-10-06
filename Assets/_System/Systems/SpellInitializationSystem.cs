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
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<SpellsDatabase>(out var database))
            return;

        Debug.Log("BDD Found!");

        ref var spellsDatabae = ref database.Blobs.Value.Spells;

        // Create entity for Spell to Index Mapper
        if (!SystemAPI.HasSingleton<SpellToIndexMap>())
        {
            var map = new NativeHashMap<SpellKey, int>(spellsDatabae.Length, Allocator.Temp);
            for (int i = 0; i < spellsDatabae.Length; i++)
            {
                map.TryAdd(new SpellKey { Value = spellsDatabae[i].ID }, i);
            }

            var mapEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(mapEntity, new SpellToIndexMap { Map = map });
        }


        var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
        var spellIndexMap = SystemAPI.GetSingleton<SpellToIndexMap>().Map;

        foreach (var (baseSpellBuffer, entity) in
                 SystemAPI.Query<DynamicBuffer<BaseSpell>>().WithEntityAccess())
        {
            if (!SystemAPI.HasBuffer<ActiveSpell>(entity))
                continue;

            var activeSpellBuffer = SystemAPI.GetBuffer<ActiveSpell>(entity);

            foreach (var spellToInit in baseSpellBuffer)
            {
                if (spellIndexMap.TryGetValue(new SpellKey { Value = spellToInit.ID }, out var spellIndex))
                {
                    activeSpellBuffer.Add(new ActiveSpell
                    {
                        DatabaseIndex = spellIndex,
                        Level = 1,
                        CooldownTimer = spellsDatabae[spellIndex].BaseCooldown
                    });
                }
            }

            ecb.RemoveComponent<BaseSpell>(entity);
        }

        ecb.Playback(state.EntityManager);
        spellIndexMap.Dispose();

        state.Enabled = false;
    }
}
