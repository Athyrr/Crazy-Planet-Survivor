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

    // Ricochet settings
    public int Bounces;
    public float BouncesSearchRadius;

    public int Pierces;

    public bool InstanciateOnce;
}
