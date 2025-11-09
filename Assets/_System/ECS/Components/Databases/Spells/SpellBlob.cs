public struct SpellBlob
{
    public ESpellID ID;
    public float BaseCooldown;
    public float BaseDamage;
    public float BaseArea;
    public float BaseRange;
    public float BaseSpeed;
    public ESpellElement Element;
    public float Lifetime;

    // Ricochet settings
    public int Bounces;
    public float BouncesSearchRadius;
}
