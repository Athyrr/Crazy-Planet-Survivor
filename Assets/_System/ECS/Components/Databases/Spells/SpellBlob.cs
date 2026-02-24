using Unity.Collections;
using Unity.Mathematics;

public struct SpellBlob
{
    // Identity
    public ESpellID ID;
    public FixedString512Bytes DisplayName;
    public ESpellTag Tag;

    // Base Stats
    public float BaseDamage;
    public float BaseCooldown;
    public float BaseSpeed;
    public float Lifetime;
    public float BaseCastRange;
    public float BaseEffectArea;
    public float3 BaseSpawnOffset;
    public float BaseSize;

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
    public int SubSpellsCount;
    public float ChildrenSpawnRadius;
    public int ChildPrefabIndex;
}
