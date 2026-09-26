using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>The shared read-only context every hit resolution needs. All lookups are read-only; the only
/// writes go through the caller's ECB.</summary>
public struct ResolveHitContext
{
    public BlobAssetReference<EffectTypeConfigBlob> EffectConfig;
    public float LifeStealConversion;
    public Entity PlayerEntity;

    [ReadOnly] public ComponentLookup<LocalToWorld> LtwLookup;
    [ReadOnly] public ComponentLookup<CoreStats> CoreStatsLookup;
    [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;

    public NativeQueue<SpellDamageEvent>.ParallelWriter DamageEventsWriter;
}

/// <summary>The single choke point for applying a hit result to a target. See §10 of SPELL_TAXONOMY.md for
/// the original "crit gruyère" history this fixed. As of this chantier, tag-based status-effect application
/// (ApplyTagEffects) is gone — effects are explicit ApplyEffect actions in the same list as the triggering
/// Damage action, resolved via ApplyMany.</summary>
public static class ResolveHit
{
    /// <summary>Applies one atomic action (no other actions from the same hit in play — used where a hit is
    /// pure Damage/Heal/ApplyBuff with no accompanying effects).</summary>
    public static void Apply(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, in HitAction action, in HitSource source, ref Random rng)
    {
        ApplyOne(in ctx, ecb, sortKey, target, in action, in source, 0f, ref rng);
    }

    /// <summary>Applies every action from one hit event, in list order. The pre-crit damage of the list's
    /// Damage action (if any) is threaded into every ApplyEffect action so Burn's magnitude formula
    /// (Ratio × hitDamage) sees the same pre-crit value the crit roll used.</summary>
    public static void ApplyMany(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, in FixedList512Bytes<HitAction> actions, in HitSource source, ref Random rng)
    {
        float triggerDamage = 0f;
        for (int i = 0; i < actions.Length; i++)
        {
            if (actions[i].Kind == EHitKind.Damage)
            {
                triggerDamage = actions[i].Damage;
                break;
            }
        }

        for (int i = 0; i < actions.Length; i++)
            ApplyOne(in ctx, ecb, sortKey, target, in actions[i], in source, triggerDamage, ref rng);
    }

    private static void ApplyOne(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, in HitAction action, in HitSource source, float triggerDamage, ref Random rng)
    {
        switch (action.Kind)
        {
            case EHitKind.Damage:
                ApplyDamage(in ctx, ecb, sortKey, target, in action, in source, ref rng, allowLifeSteal: true);
                break;

            case EHitKind.Heal:
                ecb.AppendToBuffer(sortKey, target, new HealBufferElement { Amount = (int)action.HealAmount });
                break;

            case EHitKind.ApplyBuff:
                ecb.AppendToBuffer(sortKey, target, new CharacterStatBuff
                {
                    Stat = action.Stat,
                    Value = action.StatValue,
                    Remaining = action.Duration,
                });
                ecb.AddComponent<SpellStatsCalculationRequest>(sortKey, target);
                break;

            case EHitKind.ApplyEffect:
                ApplyEffect(in ctx, ecb, sortKey, target, action.EffectType, triggerDamage, in source);
                break;
        }
    }

    /// <summary>Rolls crit, appends the damage, tracks + life-steals. <paramref name="allowLifeSteal"/> is
    /// false for a Burn tick whose EffectTypeConfig.AllowLifeSteal (+ CoreStats.BurnCanLifeSteal) is off —
    /// tracking stays unconditional either way (spec: tick tracking is inconditionnel).</summary>
    private static void ApplyDamage(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, in HitAction action, in HitSource source, ref Random rng, bool allowLifeSteal)
    {
        bool isCrit = action.CritChance > 0f && rng.NextFloat(0f, 1f) <= action.CritChance;
        float dmg = action.Damage;
        if (isCrit)
            dmg *= math.max(1f, action.CritMultiplier);

        int damageDealt = (int)dmg;

        ecb.AppendToBuffer(sortKey, target, new DamageBufferElement
        {
            Damage = damageDealt,
            Tag = action.Tags,
            IsCritical = isCrit,
            ShakeSource = source.Shake,
        });

        if (source.DatabaseIndex < 0)
            return;

        ctx.DamageEventsWriter.Enqueue(new SpellDamageEvent
        {
            DatabaseIndex = source.DatabaseIndex,
            DamageAmount = damageDealt,
        });

        if (allowLifeSteal && source.Caster == ctx.PlayerEntity && ctx.LifeStealConversion > 0f
            && ctx.ActiveSpellLookup.TryGetBuffer(ctx.PlayerEntity, out var playerSpells))
        {
            for (int li = 0; li < playerSpells.Length; li++)
            {
                if (playerSpells[li].DatabaseIndex != source.DatabaseIndex)
                    continue;

                float lsChance = playerSpells[li].FinalLifeStealChance;
                if (lsChance > 0f && rng.NextFloat() < lsChance)
                    ecb.AppendToBuffer(sortKey, ctx.PlayerEntity,
                        new LifeStealProcBufferElement { Heal = ctx.LifeStealConversion * damageDealt });
                break;
            }
        }
    }

    /// <summary>Public overload used by the Burn-tick job (ActiveEffectsSystem), which needs to gate crit
    /// and life-steal on EffectTypeConfig.AllowCrit/AllowLifeSteal (+ their CoreStats upgrade toggles).</summary>
    public static void ApplyDamage(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, in HitAction action, in HitSource source, ref Random rng, bool allowCrit, bool allowLifeSteal)
    {
        var damageAction = action;
        if (!allowCrit)
            damageAction.CritChance = 0f;
        ApplyDamage(in ctx, ecb, sortKey, target, in damageAction, in source, ref rng, allowLifeSteal);
    }

    /// <summary>Computes magnitude/duration for one effect type (global CoreStats multiplier + per-spell
    /// ActiveSpell bonus compose additively on top of the EffectTypeConfig base, matching the project's
    /// existing Damage/Size/Speed composition) and enqueues a StatusEffectApplyRequest — the actual
    /// refresh-vs-add happens later, off the parallel hot path (see ActiveEffectsSystem.DrainApplyRequestsJob).</summary>
    private static void ApplyEffect(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, EffectType type, float triggerDamage, in HitSource source)
    {
        ref var entries = ref ctx.EffectConfig.Value.Entries;
        ref readonly var cfg = ref EffectTypeConfigLookup.Get(ref entries, type);

        float magnitudeMult = 1f;
        float durationMult = 1f;
        float magnitudeBonus = 0f;

        if (source.Caster != Entity.Null && ctx.CoreStatsLookup.HasComponent(source.Caster))
        {
            var coreStats = ctx.CoreStatsLookup[source.Caster];
            switch (type)
            {
                case EffectType.Burn:
                    magnitudeMult += coreStats.GlobalBurnDamageMultiplier;
                    durationMult += coreStats.GlobalBurnDurationMultiplier;
                    break;
                case EffectType.Slow:
                    magnitudeMult += coreStats.GlobalSlowStrengthMultiplier;
                    durationMult += coreStats.GlobalSlowDurationMultiplier;
                    break;
                case EffectType.Stun:
                    durationMult += coreStats.GlobalStunDurationMultiplier;
                    break;
            }
        }

        if (source.DatabaseIndex >= 0 && source.Caster != Entity.Null
            && ctx.ActiveSpellLookup.TryGetBuffer(source.Caster, out var casterSpells))
        {
            for (int i = 0; i < casterSpells.Length; i++)
            {
                if (casterSpells[i].DatabaseIndex != source.DatabaseIndex)
                    continue;
                if (type == EffectType.Burn)
                    magnitudeBonus = casterSpells[i].FinalBurnMagnitudeBonus;
                else if (type == EffectType.Slow)
                    magnitudeBonus = casterSpells[i].FinalSlowMagnitudeBonus;
                break;
            }
        }

        float baseMagnitude = type == EffectType.Burn
            ? StatusEffectFormulas.ComputeBurnMagnitude(cfg.Ratio, triggerDamage)
            : StatusEffectFormulas.ComputeFlatMagnitude(cfg.BaseMagnitude);
        float magnitude = baseMagnitude * (magnitudeMult + magnitudeBonus);
        float duration = cfg.BaseDuration * durationMult;

        float3 direction = float3.zero;
        if (type == EffectType.Knockback && ctx.LtwLookup.HasComponent(target))
        {
            float3 targetPos = ctx.LtwLookup[target].Position;
            float3 pushDir = targetPos - source.PushOrigin;
            float d2 = math.lengthsq(pushDir);
            direction = d2 > 0.001f ? math.normalize(pushDir) : new float3(0f, 0f, 1f);
        }

        ecb.AppendToBuffer(sortKey, target, new StatusEffectApplyRequest
        {
            Type = type,
            Source = source.Emitter,
            Magnitude = magnitude,
            Duration = duration,
            Direction = direction,
            StackMode = cfg.StackMode,
            MaxStacks = cfg.MaxStacks,
        });
    }
}
