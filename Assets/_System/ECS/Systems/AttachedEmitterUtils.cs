using Unity.Entities;
using Unity.Mathematics;

public static class AttachedEmitterUtils
{
    /// <summary>Spawns one attached child: live stats (damage/crit/tags/effects read from the root at hit time),
    /// anchored analytically, registered in the owner's LinkedEntityGroup (the owner is element 0).</summary>
    public static Entity SpawnAnchoredChild(in SpellSpawnLookups lk, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity owner, Entity childPrefab, int index, Entity caster, int rootDatabaseIndex, float3 position, uint targetLayers)
    {
        var p = new SpellSpawnParams
        {
            Prefab = childPrefab, Caster = caster, DatabaseIndex = rootDatabaseIndex,
            Position = position, Rotation = quaternion.identity, Direction = new float3(0f, 0f, 1f),
            Scale = 1f, TargetLayers = targetLayers, Allowed = ESpellCapability.None,
        };
        Entity child = SpellSpawnUtils.SpawnSpellInstance(in lk, ecb, sortKey, in p);
        ecb.AddComponent(sortKey, child, new EmitterAnchor { Owner = owner, Index = index });
        ecb.AddComponent<LiveSpellStats>(sortKey, child);
        ecb.AppendToBuffer(sortKey, owner, new LinkedEntityGroup { Value = child });
        return child;
    }
}
