using Unity.Collections;
using Unity.Mathematics;

public struct SpellBlob
{
    // Identity
    public ESpellID ID;
    // public FixedString512Bytes DisplayName; // 
    public ESpellTag Tag;

    // Base Stats
    public float BaseDamage;
    public float BaseCooldown;
    public float BaseSpeed;
    public float Lifetime;
    public float BaseCastRange;
    public float BaseAreaOfEffect;
    public float3 BaseSpawnOffset;
    public float BaseSize;
    
    // Targeting
    public ESpellTargetingMode TargetingMode;

    // Ricochet
    public int Bounces;
    public float BounceRange;

    // Pierce
    public int Pierces;

    // Tick Effects
    public float TickRate;

    // Children based spells
    public float ChildrenSpawnRadius;
    public int ChildPrefabIndex;
    
    // Amount
    public int BaseAmount; 
}