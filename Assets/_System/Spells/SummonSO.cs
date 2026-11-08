using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One base stat or inheritance ratio of a Summon.</summary>
[Serializable]
public struct SummonStatValue
{
    public ECharacterStat Stat;
    public float Value;
}

/// <summary>Data of a Summon (spec allegeance-summon §2), referenced by a SpellSO of the Summon family (SpellSO.Summon).
/// Baked into SpellBlob.Summon by SpellsDatabaseAuthoring.</summary>
[CreateAssetMenu(fileName = "NewSummonData", menuName = "Survivor/Spells/Summon Data")]
public class SummonSO : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Agent prefab: visual only, carries SummonAgentAuthoring, NO collider (Summons are untouchable in this version).")]
    public GameObject AgentPrefab;

    [Tooltip("Preset that pre-fills the axes below when changed (Custom = leave them as they are).")]
    public ESummonArchetype Archetype = ESummonArchetype.Turret;

    [SerializeField, HideInInspector] private ESummonArchetype _appliedArchetype = ESummonArchetype.Turret;

    [Header("Axes — coded: Fixed / Follow, Cast, Nearest, Relocate / Permanent, no aggro")]
    public ESummonMobility Mobility = ESummonMobility.Fixed;
    public ESummonOffense Offense = ESummonOffense.Cast;
    public ESummonTargeting Targeting = ESummonTargeting.Nearest;
    public ESummonEndOfLife EndOfLife = ESummonEndOfLife.Relocate;
    [Tooltip("Modelled only: aggro comes with the summon-aggro slice.")]
    public bool TakesAggro;

    [Header("Base stats (absent = neutral)")]
    [Tooltip("Damage, AttackSpeed, SizeMultiplier, SpellSpeed, SpellDuration, CastRange: deltas (0 = neutral). " +
             "CritChance, CritDamage, LifeStealChance: additive. Speed: ABSOLUTE move speed (m/s). MaxHealth: absolute (unused until summon-aggro).")]
    public SummonStatValue[] BaseStats = new SummonStatValue[0];

    [Header("Inheritance of the owner's character stats")]
    [Range(0f, 1f)] public float InheritRatio = 0.3f;

    [Tooltip("Per-stat ratio overriding InheritRatio. Default: LifeStealChance and CritChance at 0 — a Summon only crits " +
             "through its base or an upgrade of its spell, and does not life-steal unless upgraded.")]
    public SummonStatValue[] RatioOverrides =
    {
        new SummonStatValue { Stat = ECharacterStat.LifeStealChance, Value = 0f },
        new SummonStatValue { Stat = ECharacterStat.CritChance, Value = 0f },
    };

    [Header("Attack")]
    [Tooltip("Spells the agent casts, from its OWN ActiveSpell copies (the player's upgrades never leak in). Never an enemy SO: " +
             "PF_Spell_Enemy_Projectile cannot hit an enemy.")]
    public SpellSO[] AttackSpells = new SpellSO[0];

    [Header("Placement")]
    [Tooltip("Ring around the owner where agents appear / relocate (never on the owner).")]
    [Min(0.5f)] public float RingMinRadius = 2.5f;
    [Min(0.5f)] public float RingMaxRadius = 4f;
    [Tooltip("Follow: beyond this arc distance the agent returns to its owner until ~50 % of it.")]
    [Min(1f)] public float LeashRadius = 12f;

    [Header("Movement (Follow)")]
    [Tooltip("Acceleration of the persistent velocity (u/s²), same model as the enemies.")]
    public float Acceleration = 40f;
    [Tooltip("Turn rate cap (°/s).")]
    public float MaxTurnRateDeg = 540f;

    /// <summary>Pre-fills the axes from the archetype (Turret = Fixed/Relocate, Pet = Follow/Permanent).</summary>
    public void ApplyArchetypePreset()
    {
        switch (Archetype)
        {
            case ESummonArchetype.Turret:
                Mobility = ESummonMobility.Fixed; Offense = ESummonOffense.Cast; Targeting = ESummonTargeting.Nearest;
                EndOfLife = ESummonEndOfLife.Relocate; TakesAggro = false;
                break;
            case ESummonArchetype.Pet:
                Mobility = ESummonMobility.Follow; Offense = ESummonOffense.Cast; Targeting = ESummonTargeting.Nearest;
                EndOfLife = ESummonEndOfLife.Permanent; TakesAggro = false;
                break;
        }
    }

    /// <summary>Base stat list → CoreStats (absent = 0). Speed is the absolute BaseMoveSpeed; the MoveSpeed delta is inherited.</summary>
    public CoreStats BuildBaseStats(List<string> warnings = null)
    {
        var s = new CoreStats();
        foreach (var entry in BaseStats)
        {
            switch (entry.Stat)
            {
                case ECharacterStat.Damage: s.Damage = entry.Value; break;
                case ECharacterStat.AttackSpeed: s.AttackSpeed = entry.Value; break;
                case ECharacterStat.SizeMultiplier: s.SpellSize = entry.Value; break;
                case ECharacterStat.SpellSpeed: s.SpellSpeed = entry.Value; break;
                case ECharacterStat.SpellDuration: s.SpellDuration = entry.Value; break;
                case ECharacterStat.CastRange: s.CastRange = entry.Value; break;
                case ECharacterStat.Speed: s.BaseMoveSpeed = entry.Value; break;
                case ECharacterStat.CritChance: s.CritChance = entry.Value; break;
                case ECharacterStat.CritDamage: s.CritDamage = entry.Value; break;
                case ECharacterStat.LifeStealChance: s.LifeStealChance = entry.Value; break;
                case ECharacterStat.MaxHealth: s.MaxHealth = entry.Value; break;
                default: warnings?.Add($"{name}: base stat {entry.Stat} is not a Summon stat (ignored)."); break;
            }
        }
        return s;
    }

    /// <summary>InheritRatio everywhere, then the overrides.</summary>
    public SummonInheritRatios BuildRatios(List<string> warnings = null)
    {
        var r = SummonInheritRatios.Uniform(InheritRatio);
        foreach (var o in RatioOverrides)
            if (!r.TrySet(o.Stat, o.Value))
                warnings?.Add($"{name}: ratio override {o.Stat} is not an inherited stat (ignored).");
        return r;
    }

    /// <summary>Inspector validation (spec §2: a modelled but uncoded value is a warning).</summary>
    public void CollectWarnings(List<string> warnings)
    {
        if (Mobility != ESummonMobility.Fixed && Mobility != ESummonMobility.Follow)
            warnings.Add($"{name}: Mobility {Mobility} is modelled but not coded (Fixed / Follow only).");
        if (Offense != ESummonOffense.Cast)
            warnings.Add($"{name}: Offense {Offense} is modelled but not coded (Cast only).");
        if (Targeting != ESummonTargeting.Nearest)
            warnings.Add($"{name}: Targeting {Targeting} is modelled but not coded (Nearest only).");
        if (EndOfLife == ESummonEndOfLife.DeathCooldown)
            warnings.Add($"{name}: EndOfLife DeathCooldown is modelled but not coded (slice summon-aggro).");
        if (TakesAggro)
            warnings.Add($"{name}: TakesAggro is modelled but not coded (slice summon-aggro).");
        if (AgentPrefab == null)
            warnings.Add($"{name}: AgentPrefab is missing.");
        else if (AgentPrefab.GetComponent<SummonAgentAuthoring>() == null)
            warnings.Add($"{name}: AgentPrefab has no SummonAgentAuthoring.");
        else if (AgentPrefab.GetComponentInChildren<Collider>(true) != null)
            warnings.Add($"{name}: AgentPrefab has a Collider — Summons must stay untouchable in this version.");
        if (AttackSpells == null || AttackSpells.Length == 0)
            warnings.Add($"{name}: no attack spell — the agent will never cast.");
        if (RingMinRadius > RingMaxRadius)
            warnings.Add($"{name}: RingMinRadius > RingMaxRadius.");
        BuildBaseStats(warnings);
        BuildRatios(warnings);
    }

    private void OnValidate()
    {
        if (Archetype != _appliedArchetype)
        {
            ApplyArchetypePreset();
            _appliedArchetype = Archetype;
        }

        var warnings = new List<string>();
        CollectWarnings(warnings);
        foreach (var w in warnings)
            Debug.LogWarning($"[SummonSO] {w}", this);
    }
}
