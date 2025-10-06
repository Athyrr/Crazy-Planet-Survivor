using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Represents a spell in runtime with its coolodwn and datas.
/// </summary>
public struct ActiveSpell : IBufferElementData
{
    public int DatabaseIndex;

    public float CooldownTimer;
    public int Level;

}

public struct BaseSpell : IBufferElementData
{
    public ESpellID ID;
}

public struct CastSpellRequest : IComponentData
{
    public Entity Caster;
    public Entity Target;

    //public BlobAssetReference<SpellBlobs> DatabaseRef;
    public int DatabaseIndex;

    //public ref readonly SpellBlob GetSpellData() => ref DatabaseRef.Value.Spells[DatabaseIndex];
}

public struct EnemySpellReady : IBufferElementData
{
    public Entity Caster;
    public ActiveSpell Spell;
}

public struct SpellToIndexMap : IComponentData
{
    public NativeHashMap<SpellKey, int> Map;
}

public struct SpellsDatabase : IComponentData
{
    public BlobAssetReference<SpellBlobs> Blobs;
}

public struct SpellBlobs
{
    public BlobArray<SpellBlob> Spells;
}

public struct SpellBlob
{
    public ESpellID ID;
    public float BaseCooldown;
    public float BaseDamage;
    public float BaseArea;
    public float BaseRange;
    public float BaseSpeed;
    public ESpellElement Element;
}

public struct SpellPrefab : IBufferElementData
{
    public Entity Prefab;
}

public struct FireballRequestTag : IComponentData { }
public struct IceBoltRequestTag : IComponentData { }
public struct LightningStrikeRequestTag : IComponentData { }
public struct MagicMissileRequestTag : IComponentData { }