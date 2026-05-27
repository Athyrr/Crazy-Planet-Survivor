using Unity.Entities;

public struct DamageOnTick : IComponentData
{
    public float DamagePerTick;
    public ESpellTag Tags;

    public float TickRate;
    public float ElapsedTime;

    public Entity Caster;
    public float AreaRadius;        // Runtime final size (scaled by upgrades)
    public float PrefabRadius;      // base radius from prefab

    public EAttackAreaShape Shape;
    public float HalfAngle;         // Cone: half-angle in radians
    public float SweepStart;        // Cone: sweep start relative to forward (radians)
    public float SweepEnd;          // Cone: sweep end relative to forward (radians)
    public float RingThickness;     // Ring: width of the ring band

    //public bool IsCritical;
    public float TotalCritChance;
    public float TotalCritMultiplier;

    public uint TargetLayers;
}
