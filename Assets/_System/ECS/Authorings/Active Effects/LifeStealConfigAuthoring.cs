using _System.Settings;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Bakes the life-steal rules from the shared <see cref="CpCombatEffectsSettings"/> SO into the
/// <see cref="LifeStealConfig"/> singleton. Kept separate from <see cref="ActiveEffectsConfigAuthoring"/>
/// (which bakes the enemy debuff rules) so life steal — a player self-sustain mechanic — stays its own
/// concern. Both authorings reference the same SO. Rebake SC_Entity_Core after changing the SO.
/// </summary>
public class LifeStealConfigAuthoring : MonoBehaviour
{
    [Tooltip("Shared combat effects settings SO. The life-steal section is baked from it.")]
    public CpCombatEffectsSettings Settings;

    private class Baker : Baker<LifeStealConfigAuthoring>
    {
        public override void Bake(LifeStealConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            var s = authoring.Settings;
            if (s != null)
                DependsOn(s);

            AddComponent(entity, new LifeStealConfig
            {
                // Fall back to the design defaults if the SO is unassigned, so the bake never zeroes out.
                Conversion = s != null ? s.LifeStealConversion : 0.075f,
                ProcCooldown = s != null ? s.LifeStealProcCooldown : 0.1f,
            });
        }
    }
}

/// <summary>
/// Global life-steal rules (constants, not a per-entity stat). The proc CHANCE is a stat on
/// <see cref="CoreStats"/>; this only holds the conversion ratio and the rate limiter.
/// </summary>
public struct LifeStealConfig : IComponentData
{
    /// <summary>Fraction of a hit's damage healed on a successful proc (0.075 = 7.5%).</summary>
    public float Conversion;

    /// <summary>Minimum seconds between two procs (0.1 = max 10 procs/s).</summary>
    public float ProcCooldown;
}
