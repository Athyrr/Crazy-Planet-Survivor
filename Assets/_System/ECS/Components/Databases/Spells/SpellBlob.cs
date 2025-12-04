public struct SpellBlob
{
    public ESpellID ID;
    public float BaseDamage;
    public float BaseSpeed;

    public float BaseCooldown;

    public float BaseCastRange;
    public float BaseEffectArea;

    public float BaseSpawnOffset;

    public ESpellElement Element;
    public float Lifetime;

    // Ricochet 
    public int Bounces;
    public float BouncesSearchRadius;

    // Pierce
    public int Pierces;

    // Tick Effects (for auras)
    public float BaseDamagePerTick;
    public float TickRate;
}
