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
    /// <summary>Summon runtime of the caster: life steal and damage credit go to its owner (spec allegeance-summon §1, §3).</summary>
    [ReadOnly] public ComponentLookup<Summon> SummonLookup;
    /// <summary>Existence check only (HasBuffer) — the actual append still goes through the caller's ECB.
    /// Guards ApplyEffect against a target with no StatusEffectApplyRequest buffer (e.g. the player prefab
    /// today, pending an Editor-side ActiveEffectsAuthoring addition — C2, final whole-branch review):
    /// without this, ecb.AppendToBuffer on a buffer-less entity errors at EndSimulation playback.</summary>
    [ReadOnly] public BufferLookup<StatusEffectApplyRequest> RequestLookup;

    /// <summary>Global emitter rules (split falloff, explosion life steal); EmitterConfig.Default when the
    /// singleton is missing.</summary>
    public EmitterConfig Emitter;

    public NativeQueue<SpellDamageEvent>.ParallelWriter DamageEventsWriter;
}

/// <summary>The single choke point for applying a hit result to a target. See §10 of SPELL_TAXONOMY.md for
/// the original "crit gruyère" history this fixed. As of this chantier, tag-based status-effect application
/// (ApplyTagEffects) is gone — effects are explicit ApplyEffect actions in the same list as the triggering
/// Damage action, resolved via ApplyMany.</summary>
public static class ResolveHit
{
    /// <summary>Damage immunity of a target, the single test every hit path uses to fill
    /// <see cref="HitSource.DamageImmune"/>: the <see cref="Invincible"/> tag, or <see cref="GlobalIFrames"/>
    /// enabled (player-only, baked by PlayerAuthoring). An immune target still RECEIVES the hit (effects,
    /// emitters) with 0 damage. Dash i-frames are NOT tested here: the call site skips a dashing target.</summary>
    public static bool IsDamageImmune(Entity target, in ComponentLookup<Invincible> invincibleLookup,
        in ComponentLookup<GlobalIFrames> globalIFramesLookup)
    {
        return invincibleLookup.HasComponent(target)
               || (globalIFramesLookup.HasComponent(target) && globalIFramesLookup.IsComponentEnabled(target));
    }

    /// <summary>Applies one atomic action (no other actions from the same hit in play — used where a hit is
    /// pure Damage/Heal/ApplyBuff with no accompanying effects).</summary>
    public static void Apply(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, in HitAction action, in HitSource source, ref Random rng)
    {
        ApplyOne(in ctx, ecb, sortKey, target, in action, in source, 0f, ref rng);
    }

    /// <summary>Applies every action from one hit event, in list order. The Damage action (and the triggerDamage
    /// threaded into ApplyEffect, so Burn follows) is scaled by SplitDamageFalloff^Generation. Returns the crit
    /// result of the Damage action (Explode children inherit it).</summary>
    public static bool ApplyMany(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, in FixedList512Bytes<HitAction> actions, in HitSource source, ref Random rng)
    {
        float falloff = EmitterMath.Falloff(ctx.Emitter.SplitDamageFalloff, source.Generation);

        float triggerDamage = 0f;
        for (int i = 0; i < actions.Length; i++)
        {
            if (actions[i].Kind == EHitKind.Damage)
            {
                triggerDamage = actions[i].Damage * falloff;
                break;
            }
        }

        bool crit = false;
        for (int i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            if (action.Kind == EHitKind.Damage)
                action.Damage *= falloff;
            crit |= ApplyOne(in ctx, ecb, sortKey, target, in action, in source, triggerDamage, ref rng);
        }
        return crit;
    }

    private static bool ApplyOne(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, in HitAction action, in HitSource source, float triggerDamage, ref Random rng)
    {
        // HitAction.Target (§9.4): Self lands on the caster; every other value on the target the call site selected
        // (selection by layers stays at the call site). No producer of Self today: no visible change.
        Entity recipient = action.Target == EAllegiance.Self && source.Caster != Entity.Null ? source.Caster : target;
        switch (action.Kind)
        {
            case EHitKind.Damage:
                return ApplyDamage(in ctx, ecb, sortKey, recipient, in action, in source, ref rng, allowLifeSteal: true);
            case EHitKind.Heal:
                ecb.AppendToBuffer(sortKey, recipient, new HealBufferElement { Amount = (int)action.HealAmount });
                return false;
            case EHitKind.ApplyBuff:
                ecb.AppendToBuffer(sortKey, recipient, new CharacterStatBuff { Stat = action.Stat, Value = action.StatValue, Remaining = action.Duration });
                ecb.AddComponent<SpellStatsCalculationRequest>(sortKey, recipient);
                return false;
            case EHitKind.ApplyEffect:
                ApplyEffect(in ctx, ecb, sortKey, recipient, action.EffectType, triggerDamage, in source);
                return false;
        }
        return false;
    }

    /// <summary>Rolls (or applies the imposed) crit, appends the damage, tracks + life-steals. Returns the crit
    /// result even for a damage-immune target (an Explode child still inherits it). <paramref name="allowLifeSteal"/>
    /// is false for a Burn tick whose EffectTypeConfig.AllowLifeSteal (+ CoreStats.BurnCanLifeSteal) is off —
    /// tracking stays unconditional either way.</summary>
    private static bool ApplyDamage(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, in HitAction action, in HitSource source, ref Random rng, bool allowLifeSteal)
    {
        bool isCrit;
        switch (source.CritMode)
        {
            case ECritMode.ForceCrit: isCrit = true; break;
            case ECritMode.ForceNoCrit: isCrit = false; break;
            default: isCrit = action.CritChance > 0f && rng.NextFloat(0f, 1f) <= action.CritChance; break;
        }

        // Damage-immune target (Invincible / GlobalIFrames): the hit is received (effects run in ApplyMany),
        // only the damage, its tracking and its life steal are skipped.
        if (source.DamageImmune)
            return isCrit;

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
            return isCrit;

        ctx.DamageEventsWriter.Enqueue(new SpellDamageEvent { DatabaseIndex = source.DatabaseIndex, DamageAmount = damageDealt, Caster = source.Caster });

        // Life steal: the chance comes from the CASTER's own ActiveSpell (a Summon's attack spell: 0 % by default), the
        // heal goes to the player — the caster itself, or the owner of a Summon. Identical to eee06e4e for the player.
        if (allowLifeSteal && !IsLifeStealBlocked(in ctx, in source) && ctx.LifeStealConversion > 0f
            && TryGetLifeStealHealer(in ctx, source.Caster, out Entity healer)
            && ctx.ActiveSpellLookup.TryGetBuffer(source.Caster, out var casterSpells))
        {
            for (int li = 0; li < casterSpells.Length; li++)
            {
                if (casterSpells[li].DatabaseIndex != source.DatabaseIndex)
                    continue;
                float lsChance = casterSpells[li].FinalLifeStealChance;
                if (lsChance > 0f && rng.NextFloat() < lsChance)
                    ecb.AppendToBuffer(sortKey, healer, new LifeStealProcBufferElement { Heal = ctx.LifeStealConversion * damageDealt });
                break;
            }
        }
        return isCrit;
    }

    /// <summary>Explosion life steal (model of BurnCanLifeSteal): blocked unless the global config or the caster's
    /// CoreStats.ExplosionCanLifeSteal re-enables it. Read here only, live (an upgrade picked mid-explosion applies).</summary>
    private static bool IsLifeStealBlocked(in ResolveHitContext ctx, in HitSource source)
    {
        if (!source.LifeStealBlocked || ctx.Emitter.ExplosionAllowLifeSteal)
            return false;
        if (source.Caster != Entity.Null && ctx.CoreStatsLookup.TryGetComponent(source.Caster, out var cs) && cs.ExplosionCanLifeSteal > 0f)
            return false;
        return true;
    }

    /// <summary>Who a life-steal proc heals: the player for its own hits, the owner for a Summon of the player; nobody else.</summary>
    private static bool TryGetLifeStealHealer(in ResolveHitContext ctx, Entity caster, out Entity healer)
    {
        healer = Entity.Null;
        if (caster == Entity.Null || ctx.PlayerEntity == Entity.Null)
            return false;
        if (caster == ctx.PlayerEntity)
        {
            healer = caster;
            return true;
        }
        if (ctx.SummonLookup.TryGetComponent(caster, out var summon) && summon.Owner == ctx.PlayerEntity)
        {
            healer = summon.Owner;
            return true;
        }
        return false;
    }

    /// <summary>Public overload used by the Burn-tick job (ActiveEffectsSystem), which needs to gate crit
    /// and life-steal on EffectTypeConfig.AllowCrit/AllowLifeSteal (+ their CoreStats upgrade toggles).
    /// Returns the crit result.</summary>
    public static bool ApplyDamage(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, in HitAction action, in HitSource source, ref Random rng, bool allowCrit, bool allowLifeSteal)
    {
        var damageAction = action;
        if (!allowCrit)
            damageAction.CritChance = 0f;
        return ApplyDamage(in ctx, ecb, sortKey, target, in damageAction, in source, ref rng, allowLifeSteal);
    }

    /// <summary>Computes magnitude/duration for one effect type (global CoreStats multiplier + per-spell
    /// ActiveSpell bonus compose additively on top of the EffectTypeConfig base, matching the project's
    /// existing Damage/Size/Speed composition) and enqueues a StatusEffectApplyRequest — the actual
    /// refresh-vs-add happens later, off the parallel hot path (see ActiveEffectsSystem.DrainApplyRequestsJob).</summary>
    private static void ApplyEffect(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, EffectType type, float triggerDamage, in HitSource source)
    {
        // C2 guard: a target with no StatusEffectApplyRequest buffer (player prefab today — Editor step
        // pending) silently no-ops instead of erroring at ECB playback — same graceful behavior
        // HazardZoneSystem.cs's ApplyBurn already has for this exact case.
        if (!ctx.RequestLookup.HasBuffer(target))
            return;

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
