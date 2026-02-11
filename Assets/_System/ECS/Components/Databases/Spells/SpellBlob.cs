using Unity.Collections;
using Unity.Mathematics;

public struct SpellBlob
{
    public FixedString512Bytes DisplayName;

    public ESpellID ID;
    public float BaseDamage;
    public float BaseSpeed;

    public float BaseCooldown;

    public float BaseCastRange;
    public float BaseEffectArea;

    public float3 BaseSpawnOffset;

    public ESpellTag Tag;
    public float Lifetime;

    // Targeting
    public ESpellTargetingMode TargetingMode;

    // Ricochet 
    public int Bounces;
    public float BouncesSearchRadius;

    // Pierce
    public int Pierces;

    // Tick Effects (for auras)
    public float BaseDamagePerTick;
    public float TickRate;

    // Children based spells
    public int ChildPrefabIndex;
    public int ChildrenCount;
    public float ChildrenSpawnRadius;
}
