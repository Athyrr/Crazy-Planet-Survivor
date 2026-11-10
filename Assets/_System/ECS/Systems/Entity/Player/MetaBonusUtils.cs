using Unity.Entities;

/// <summary>Meta-progression bonus mapping, shared by PlayerSpawnerSystem (spawn) and the sandbox reset_stats. A meta
/// stat without a case here is a no-op; integer stats take whole numbers ((int) cast), as before.</summary>
public static class MetaBonusUtils
{
    // todo handle while several bonuses have to be applied
    public static void ApplyMetaBonus(ref ApplyMetaProgressionRequest request, ECharacterStat stat, float bonus)
    {
        switch (stat)
        {
            case ECharacterStat.MaxHealth:      request.MaxHealthBonus += bonus; break;
            case ECharacterStat.Speed:          request.MoveSpeedBonus += bonus; break;
            case ECharacterStat.Damage:         request.DamageBonus += bonus; break;
            case ECharacterStat.Armor:          request.ArmorBonus += bonus; break;
            case ECharacterStat.AttackSpeed:    request.AttackSpeedBonus += bonus; break;
            case ECharacterStat.SizeMultiplier: request.SpellSizeBonus += bonus; break;
            case ECharacterStat.CollectRange:   request.PickupRangeBonus += bonus; break;
            case ECharacterStat.BounceCount:    request.BounceBonus += (int)bonus; break;
            case ECharacterStat.PierceCount:    request.PierceBonus += (int)bonus; break;
            case ECharacterStat.CritChance:     request.CritChanceBonus += bonus; break;
            case ECharacterStat.CritDamage:     request.CritDamageBonus += bonus; break;
            case ECharacterStat.SpellSpeed:     request.SpellSpeedBonus += bonus; break;
            case ECharacterStat.SpellDuration:  request.SpellDurationBonus += bonus; break;
            case ECharacterStat.CastRange:      request.CastRangeBonus += bonus; break;
            case ECharacterStat.Amount:         request.AmountBonus += (int)bonus; break;
            case ECharacterStat.Luck:           request.LuckBonus += bonus; break;
            case ECharacterStat.HealthRegen:    request.HealthRegenBonus += bonus; break;
        }
    }

    public static ApplyMetaProgressionRequest Build(DynamicBuffer<MetaProgressionLevelElement> metaBuffer)
    {
        var request = new ApplyMetaProgressionRequest();
        for (int i = 0; i < metaBuffer.Length; i++)
        {
            var meta = metaBuffer[i];
            if (meta.Level <= 0 || meta.TotalBonus == 0f)
                continue;
            ApplyMetaBonus(ref request, meta.Stat, meta.TotalBonus);
        }
        return request;
    }
}
