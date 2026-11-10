#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// <summary>ECharacterStat ↔ CoreStats field, the same mapping as ApplyUpgradeSystem.ApplyPlayerStatUpgrade, in absolute
/// values. Stats without a CoreStats field (Health, Dash* flags on DashEffect, None) are not settable.</summary>
public static class SandboxStatMap
{
    public static readonly ECharacterStat[] Settable =
    {
        ECharacterStat.MaxHealth, ECharacterStat.HealthRegen, ECharacterStat.Armor, ECharacterStat.KnockbackResistance,
        ECharacterStat.Speed, ECharacterStat.CollectRange, ECharacterStat.Damage, ECharacterStat.AttackSpeed,
        ECharacterStat.SizeMultiplier, ECharacterStat.SpellSpeed, ECharacterStat.SpellDuration, ECharacterStat.CastRange,
        ECharacterStat.Amount, ECharacterStat.PierceCount, ECharacterStat.BounceCount, ECharacterStat.CritChance,
        ECharacterStat.CritDamage, ECharacterStat.Luck, ECharacterStat.LifeStealChance, ECharacterStat.SummonPower,
        ECharacterStat.BurnDamage, ECharacterStat.BurnDuration, ECharacterStat.SlowStrength, ECharacterStat.SlowDuration,
        ECharacterStat.StunDuration, ECharacterStat.BurnCanCrit, ECharacterStat.BurnCanLifeSteal,
        ECharacterStat.ExplosionCanLifeSteal, ECharacterStat.DashCount, ECharacterStat.DashCooldown,
    };

    public static bool IsInteger(ECharacterStat stat) =>
        stat == ECharacterStat.Amount || stat == ECharacterStat.PierceCount
        || stat == ECharacterStat.BounceCount || stat == ECharacterStat.DashCount;

    public static bool TryGet(in CoreStats c, ECharacterStat stat, out float value)
    {
        switch (stat)
        {
            case ECharacterStat.MaxHealth: value = c.MaxHealth; return true;
            case ECharacterStat.HealthRegen: value = c.HealthRegen; return true;
            case ECharacterStat.Armor: value = c.Armor; return true;
            case ECharacterStat.KnockbackResistance: value = c.KnockbackResistance; return true;
            case ECharacterStat.Speed: value = c.MoveSpeed; return true;
            case ECharacterStat.CollectRange: value = c.PickupRange; return true;
            case ECharacterStat.Damage: value = c.Damage; return true;
            case ECharacterStat.AttackSpeed: value = c.AttackSpeed; return true;
            case ECharacterStat.SizeMultiplier: value = c.SpellSize; return true;
            case ECharacterStat.SpellSpeed: value = c.SpellSpeed; return true;
            case ECharacterStat.SpellDuration: value = c.SpellDuration; return true;
            case ECharacterStat.CastRange: value = c.CastRange; return true;
            case ECharacterStat.Amount: value = c.Amount; return true;
            case ECharacterStat.PierceCount: value = c.Pierce; return true;
            case ECharacterStat.BounceCount: value = c.Bounce; return true;
            case ECharacterStat.CritChance: value = c.CritChance; return true;
            case ECharacterStat.CritDamage: value = c.CritDamage; return true;
            case ECharacterStat.Luck: value = c.Luck; return true;
            case ECharacterStat.LifeStealChance: value = c.LifeStealChance; return true;
            case ECharacterStat.SummonPower: value = c.SummonPower; return true;
            case ECharacterStat.BurnDamage: value = c.GlobalBurnDamageMultiplier; return true;
            case ECharacterStat.BurnDuration: value = c.GlobalBurnDurationMultiplier; return true;
            case ECharacterStat.SlowStrength: value = c.GlobalSlowStrengthMultiplier; return true;
            case ECharacterStat.SlowDuration: value = c.GlobalSlowDurationMultiplier; return true;
            case ECharacterStat.StunDuration: value = c.GlobalStunDurationMultiplier; return true;
            case ECharacterStat.BurnCanCrit: value = c.BurnCanCrit; return true;
            case ECharacterStat.BurnCanLifeSteal: value = c.BurnCanLifeSteal; return true;
            case ECharacterStat.ExplosionCanLifeSteal: value = c.ExplosionCanLifeSteal; return true;
            case ECharacterStat.DashCount: value = c.DashCount; return true;
            case ECharacterStat.DashCooldown: value = c.DashCooldown; return true;
            default: value = 0f; return false;
        }
    }

    public static bool TrySet(ref CoreStats c, ECharacterStat stat, float value)
    {
        int whole = UnityEngine.Mathf.RoundToInt(value);
        switch (stat)
        {
            case ECharacterStat.MaxHealth: c.MaxHealth = value; return true;
            case ECharacterStat.HealthRegen: c.HealthRegen = value; return true;
            case ECharacterStat.Armor: c.Armor = value; return true;
            case ECharacterStat.KnockbackResistance: c.KnockbackResistance = UnityEngine.Mathf.Clamp01(value); return true;
            case ECharacterStat.Speed: c.MoveSpeed = value; return true;
            case ECharacterStat.CollectRange: c.PickupRange = value; return true;
            case ECharacterStat.Damage: c.Damage = value; return true;
            case ECharacterStat.AttackSpeed: c.AttackSpeed = value; return true;
            case ECharacterStat.SizeMultiplier: c.SpellSize = value; return true;
            case ECharacterStat.SpellSpeed: c.SpellSpeed = value; return true;
            case ECharacterStat.SpellDuration: c.SpellDuration = value; return true;
            case ECharacterStat.CastRange: c.CastRange = value; return true;
            case ECharacterStat.Amount: c.Amount = whole; return true;
            case ECharacterStat.PierceCount: c.Pierce = whole; return true;
            case ECharacterStat.BounceCount: c.Bounce = whole; return true;
            case ECharacterStat.CritChance: c.CritChance = value; return true;
            case ECharacterStat.CritDamage: c.CritDamage = value; return true;
            case ECharacterStat.Luck: c.Luck = value; return true;
            case ECharacterStat.LifeStealChance: c.LifeStealChance = value; return true;
            case ECharacterStat.SummonPower: c.SummonPower = value; return true;
            case ECharacterStat.BurnDamage: c.GlobalBurnDamageMultiplier = value; return true;
            case ECharacterStat.BurnDuration: c.GlobalBurnDurationMultiplier = value; return true;
            case ECharacterStat.SlowStrength: c.GlobalSlowStrengthMultiplier = value; return true;
            case ECharacterStat.SlowDuration: c.GlobalSlowDurationMultiplier = value; return true;
            case ECharacterStat.StunDuration: c.GlobalStunDurationMultiplier = value; return true;
            case ECharacterStat.BurnCanCrit: c.BurnCanCrit = value; return true;
            case ECharacterStat.BurnCanLifeSteal: c.BurnCanLifeSteal = value; return true;
            case ECharacterStat.ExplosionCanLifeSteal: c.ExplosionCanLifeSteal = value; return true;
            case ECharacterStat.DashCount: c.DashCount = whole; return true;
            case ECharacterStat.DashCooldown: c.DashCooldown = UnityEngine.Mathf.Max(0.1f, value); return true;
            default: return false;
        }
    }
}
#endif
