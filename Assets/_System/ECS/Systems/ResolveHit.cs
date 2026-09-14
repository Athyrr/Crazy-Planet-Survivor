using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// The shared read-only context every hit resolution needs: the effect config, the effect-component
/// lookups (read to decide add-vs-refresh), the player's active-spell buffer (life-steal chance by DB
/// index) and the per-system damage-tracking queue. Built once per <c>OnUpdate</c> and copied into each
/// job that resolves hits. All lookups are read-only; the only writes go through the caller's ECB.
/// </summary>
public struct ResolveHitContext
{
    public ActiveEffectsConfig EffectsConfig;
    public float LifeStealConversion;
    public Entity PlayerEntity;

    [ReadOnly] public ComponentLookup<SlowEffect> SlowLookup;
    [ReadOnly] public ComponentLookup<StunEffect> StunLookup;
    [ReadOnly] public ComponentLookup<BurnEffect> BurnLookup;
    [ReadOnly] public ComponentLookup<ActiveKnockback> KnockbackLookup;
    [ReadOnly] public ComponentLookup<LocalToWorld> LtwLookup;
    [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;

    public NativeQueue<SpellDamageEvent>.ParallelWriter DamageEventsWriter;
}

/// <summary>
/// The single choke point for applying a hit result to a target. Every delivery family (projectile
/// contact, area Burst, area OverTime) routes here instead of re-implementing crit → damage buffer →
/// tag effects → life steal → tracking. That duplication is what produced the "crit gruyère" (§11):
/// the Area path rolled a <i>cosmetic</i> crit (multiplier never applied) and the OverTime tick never
/// rolled at all. Here crit is <b>always rolled and the multiplier applied in one place</b>.
///
/// <para>Not a system — a static helper called from inside Burst jobs. Writes go through the caller's
/// <see cref="EntityCommandBuffer.ParallelWriter"/> and <paramref name="sortKey"/>; the caller owns the
/// RNG stream (crit and life-steal draw from it) so seeding stays a call-site concern.</para>
///
/// See §10 of <c>SPELL_TAXONOMY.md</c>.
/// </summary>
public static class ResolveHit
{
    /// <summary>Applies one atomic <see cref="HitAction"/> to <paramref name="target"/>.</summary>
    public static void Apply(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, in HitAction action, in HitSource source, ref Random rng)
    {
        switch (action.Kind)
        {
            case EHitKind.Damage:
                ApplyDamage(in ctx, ecb, sortKey, target, in action, in source, ref rng);
                break;

            case EHitKind.Heal:
                // Producers apply their own carryover before building the action (Health is int, §9.1).
                ecb.AppendToBuffer(sortKey, target, new HealBufferElement { Amount = (int)action.HealAmount });
                break;

            case EHitKind.ApplyBuff:
                ecb.AppendToBuffer(sortKey, target, new CharacterStatBuff
                {
                    Stat = action.Stat,
                    Value = action.StatValue,
                    Remaining = action.Duration,
                });
                // Producer contract (§9.2): trigger the on-demand spell recalc so cached FinalX picks up the delta.
                ecb.AddComponent<SpellStatsCalculationRequest>(sortKey, target);
                break;
        }
    }

    private static void ApplyDamage(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, in HitAction action, in HitSource source, ref Random rng)
    {
        // ── Crit: rolled AND applied, the one place. (Fixes the gruyère.) ──
        bool isCrit = action.CritChance > 0f && rng.NextFloat(0f, 1f) <= action.CritChance;
        float dmg = action.Damage;
        if (isCrit)
            dmg *= math.max(1f, action.CritMultiplier);

        // int truncation kept here, in the single place — the future "float until display" migration (§11)
        // lands on this one line instead of four.
        int damageDealt = (int)dmg;

        ecb.AppendToBuffer(sortKey, target, new DamageBufferElement
        {
            Damage = damageDealt,
            Tag = action.Tags,
            IsCritical = isCrit,
            ShakeSource = source.Shake,
        });

        // Burn scales off base (pre-crit) damage, matching the pre-refactor behavior of both paths.
        ApplyTagEffects(in ctx, ecb, sortKey, target, action.Tags, action.Damage, source.PushOrigin);

        // Damage tracking + player life steal, keyed by the source spell's DB index.
        if (source.DatabaseIndex >= 0)
        {
            ctx.DamageEventsWriter.Enqueue(new SpellDamageEvent
            {
                DatabaseIndex = source.DatabaseIndex,
                DamageAmount = damageDealt,
            });

            if (source.Caster == ctx.PlayerEntity && ctx.LifeStealConversion > 0f
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
    }

    /// <summary>Applies / refreshes the status effects encoded in <paramref name="tags"/> on one target.
    /// Reuses a baked (disabled) effect component when present, otherwise adds it. Knockback pushes the
    /// target away from <paramref name="pushOrigin"/>. (Absorbs the former per-path <c>ApplyZoneEffects</c>
    /// and the inline copy in <c>CollisionSystem</c>.)</summary>
    private static void ApplyTagEffects(in ResolveHitContext ctx, EntityCommandBuffer.ParallelWriter ecb, int sortKey,
        Entity target, ESpellTag tags, float burnBaseDamage, float3 pushOrigin)
    {
        var cfg = ctx.EffectsConfig;

        if ((tags & ESpellTag.Slow) != 0)
        {
            var slow = new SlowEffect { SpeedReductionMultiplier = cfg.BaseSlowMultiplier, DurationLeft = cfg.SlowDuration };
            if (ctx.SlowLookup.HasComponent(target))
            {
                ecb.SetComponent(sortKey, target, slow);
                ecb.SetComponentEnabled<SlowEffect>(sortKey, target, true);
            }
            else ecb.AddComponent(sortKey, target, slow);
        }

        if ((tags & ESpellTag.Stun) != 0)
        {
            var stun = new StunEffect { DurationLeft = cfg.StunDuration };
            if (ctx.StunLookup.HasComponent(target))
            {
                ecb.SetComponent(sortKey, target, stun);
                ecb.SetComponentEnabled<StunEffect>(sortKey, target, true);
            }
            else ecb.AddComponent(sortKey, target, stun);
        }

        if ((tags & ESpellTag.Burn) != 0)
        {
            var burn = new BurnEffect
            {
                DamageOnTick = cfg.BurnDamageRatio * burnBaseDamage,
                TickRate = cfg.BurnTickRate,
                TickTimer = 0f,
                RemainingTime = cfg.BurnDuration,
            };
            if (ctx.BurnLookup.HasComponent(target))
            {
                ecb.SetComponent(sortKey, target, burn);
                ecb.SetComponentEnabled<BurnEffect>(sortKey, target, true);
            }
            else ecb.AddComponent(sortKey, target, burn);
        }

        if ((tags & ESpellTag.Knockback) != 0 && ctx.LtwLookup.HasComponent(target))
        {
            float3 targetPos = ctx.LtwLookup[target].Position;
            float3 pushDir = targetPos - pushOrigin;
            float d2 = math.lengthsq(pushDir);
            pushDir = d2 > 0.001f ? math.normalize(pushDir) : new float3(0f, 0f, 1f);

            var kb = new ActiveKnockback
            {
                Direction = pushDir,
                InitialForce = cfg.KnockbackForce,
                DurationLeft = cfg.KnockbackDuration,
                MaxDuration = cfg.KnockbackDuration,
            };
            if (ctx.KnockbackLookup.HasComponent(target))
            {
                ecb.SetComponent(sortKey, target, kb);
                ecb.SetComponentEnabled<ActiveKnockback>(sortKey, target, true);
            }
            else ecb.AddComponent(sortKey, target, kb);
        }
    }
}
