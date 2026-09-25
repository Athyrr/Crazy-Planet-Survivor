using Unity.Collections;
using Unity.Entities;

public struct DamageOnContact : IComponentData, IEnableableComponent
{
    public float Damage;
    public ESpellTag Tags;

    /// <summary>Status effects to apply on hit, composed once at cast-stats time by
    /// SpellStatsCalculationSystem (base SpellBlob.Effects[] + upgrade-granted ActiveSpell.AddedEffects,
    /// Task 19c) and copied here by SpellCastingSystem (Task 19d). CollisionSystem builds one
    /// HitAction.ApplyEffect per entry at hit time — never re-derived from Tags (spec §4.3 correction).</summary>
    public FixedList32Bytes<EffectSpec> EffectsToApply;

    public float AreaRadius;

    // public bool IsCritical;
    public float TotalCritChance;
    public float TotalCritMultiplier;
    public uint TargetLayers;
}