using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;
using Unity.Jobs;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(SpellCastingSystem))]
[BurstCompile]
public partial struct SpellStatsCalculationSystem : ISystem
{
    private EntityQuery _calculationRequestQuery;
    private EntityQuery _activeTickDamageSpellQuery;

    private ComponentLookup<CoreStats> _statsLookup;

    private BufferLookup<ActiveSpell> _activeSpellLookup;
    private BufferLookup<SpellStatUpgrade> _spellStatUpgradeLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<CoreStats>();
        state.RequireForUpdate<SpellsDatabase>();

        _calculationRequestQuery = SystemAPI
            .QueryBuilder()
            .WithAll<SpellStatsCalculationRequest, CoreStats, ActiveSpell, SpellStatUpgrade, CharacterStatBuff>()
            .Build();

        _activeTickDamageSpellQuery = SystemAPI.QueryBuilder()
            .WithAllRW<AreaAttack>()
            .WithAllRW<LocalTransform>()
            .WithAll<SpellSource>()
            .Build();

        _statsLookup = state.GetComponentLookup<CoreStats>(true);

        _activeSpellLookup = state.GetBufferLookup<ActiveSpell>(true);
        _spellStatUpgradeLookup = state.GetBufferLookup<SpellStatUpgrade>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (_calculationRequestQuery.IsEmpty)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecbCalculate = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var spellBlobs = SystemAPI.GetSingleton<SpellsDatabase>().Blobs;

        _activeSpellLookup.Update(ref state);
        _statsLookup.Update(ref state);
        _spellStatUpgradeLookup.Update(ref state);

        // Calculate spells stats
        var calculateSpellStatsJob = new CalculateSpellStatsJob()
        {
            ECB = ecbCalculate.AsParallelWriter(),
            SpellsDatabaseRef = spellBlobs
        };
        JobHandle calculateSpellStatsJobHandle =
            calculateSpellStatsJob.ScheduleParallel(_calculationRequestQuery, state.Dependency);

        // Update active tick spells (eg. Frozen zone)
        var updateTickDamageSpellJob = new UpdateTickDamageSpellsJob
        {
            ActiveSpellLookup = _activeSpellLookup,
        };
        JobHandle updateTickDamageSpellJobHandle =
            updateTickDamageSpellJob.ScheduleParallel(_activeTickDamageSpellQuery, calculateSpellStatsJobHandle);

        state.Dependency = updateTickDamageSpellJobHandle;
    }

    [WithAll(typeof(SpellStatsCalculationRequest))]
    [BurstCompile]
    private partial struct CalculateSpellStatsJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellsDatabaseRef;

        private void Execute(
            [ChunkIndexInQuery] int chunkIndex,
            Entity entity,
            in CoreStats coreStats,
            ref DynamicBuffer<ActiveSpell> activeSpells,
            in DynamicBuffer<SpellStatUpgrade> spellStatUpgrades,
            in DynamicBuffer<CharacterStatBuff> characterBuffs)
        {
            ref var blobSpells = ref SpellsDatabaseRef.Value.Spells;

            // Sum active temporary buffs per stat once (independent of spells). Additive with
            // CoreStats deltas below — never multiplicative (anti-exploit, §9.2). Integer stats
            // (Amount/Pierce/Bounce) truncate any fractional buff value.
            float bDamage = 0f, bAttackSpeed = 0f, bSpellSize = 0f, bSpellSpeed = 0f,
                  bSpellDuration = 0f, bCastRange = 0f,
                  bAmount = 0f, bBounce = 0f, bPierce = 0f,
                  bCritChance = 0f, bCritDamage = 0f, bLifeSteal = 0f;
            for (int b = 0; b < characterBuffs.Length; b++)
            {
                var buff = characterBuffs[b];
                switch (buff.Stat)
                {
                    case ECharacterStat.Damage:          bDamage        += buff.Value; break;
                    case ECharacterStat.AttackSpeed:     bAttackSpeed   += buff.Value; break;
                    case ECharacterStat.SizeMultiplier:  bSpellSize     += buff.Value; break;
                    case ECharacterStat.SpellSpeed:      bSpellSpeed    += buff.Value; break;
                    case ECharacterStat.SpellDuration:   bSpellDuration += buff.Value; break;
                    case ECharacterStat.CastRange:       bCastRange     += buff.Value; break;
                    case ECharacterStat.Amount:          bAmount        += buff.Value; break;
                    case ECharacterStat.BounceCount:     bBounce        += buff.Value; break;
                    case ECharacterStat.PierceCount:     bPierce        += buff.Value; break;
                    case ECharacterStat.CritChance:      bCritChance    += buff.Value; break;
                    case ECharacterStat.CritDamage:      bCritDamage    += buff.Value; break;
                    case ECharacterStat.LifeStealChance: bLifeSteal     += buff.Value; break;
                }
            }

            for (int i = 0; i < activeSpells.Length; i++)
            {
                var spell = activeSpells[i];
                ref var baseSpellData = ref blobSpells[spell.DatabaseIndex];

                // 1) Compose effectiveEffects = base SpellBlob.Effects[] + this spell's upgrade-granted AddedEffects,
                // deduped by EffectType. Must happen before step 3 below (see this task's "Ordering constraint").
                var effectiveEffects = new FixedList32Bytes<EffectSpec>();
                ref var baseEffects = ref baseSpellData.Effects;
                for (int e = 0; e < baseEffects.Length; e++)
                    effectiveEffects.Add(baseEffects[e]);
                for (int e = 0; e < spell.AddedEffects.Length; e++)
                {
                    EffectType addedType = spell.AddedEffects[e].Type;
                    bool alreadyPresent = false;
                    for (int k = 0; k < effectiveEffects.Length; k++)
                    {
                        if (effectiveEffects[k].Type == addedType) { alreadyPresent = true; break; }
                    }
                    if (!alreadyPresent)
                        effectiveEffects.Add(spell.AddedEffects[e]);
                }

                // 2) Derive Tags' 4 status bits from effectiveEffects — never the other way around. Explosive is derived
                // from the Explode capability (Task 7); an Explosive bit in AddedTags (legacy grant) is ignored; the
                // authored base bit is kept (Q14). AddedTags' own copy of the 4 STATUS bits (from a status-granting Upgrade_Spell_* asset's
                // RequiredTags, e.g. Upgrade_Spell_Fireball_Burn.asset:21, still RequiredTags=262144 after Task 20b —
                // intentionally left in place, not stripped) is masked out here so effectiveEffects stays the single
                // source of truth for those 4 bits specifically.
                const ESpellTag StatusBitsMask = ESpellTag.Burn | ESpellTag.Slow | ESpellTag.Stun | ESpellTag.Knockback;
                ESpellTag derivedStatusBits = ESpellTag.None;
                for (int e = 0; e < effectiveEffects.Length; e++)
                {
                    switch (effectiveEffects[e].Type)
                    {
                        case EffectType.Burn:      derivedStatusBits |= ESpellTag.Burn;      break;
                        case EffectType.Slow:      derivedStatusBits |= ESpellTag.Slow;      break;
                        case EffectType.Stun:      derivedStatusBits |= ESpellTag.Stun;      break;
                        case EffectType.Knockback: derivedStatusBits |= ESpellTag.Knockback; break;
                    }
                }

                // Multipliers
                // Total = 1 + Global(Player) + Buff(Player, temp) + Local(Spell)  (all stored as deltas, neutral = 0)
                float dmgMult = 1f + coreStats.Damage + bDamage + spell.LocalDamageBonusMultiplier;
                float sizeMult = 1f + coreStats.SpellSize + bSpellSize + spell.LocalSizeBonusMultiplier;
                float speedMult = 1f + coreStats.SpellSpeed + bSpellSpeed + spell.LocalSpeedBonusMultiplier;
                float durationMult = 1f + coreStats.SpellDuration + bSpellDuration + spell.LocalSpellDurationBonusMultiplier;
                float rangeMult = 1f + coreStats.CastRange + bCastRange + spell.LocalRangeBonusMultiplier;
                float tickRateMult = 1f + spell.LocalTickRateBonusMultiplier;

                float bounceRangeMult =
                    (1 + spell.LocalBounceRangeBonusMultiplier);
                // todo add global bounce range multiplier if needed

                // AttackSpeed is already a delta (0 = normal), no +1 needed here
                float cdReductionMult = coreStats.AttackSpeed + bAttackSpeed +
                                        spell.LocalCooldownReducBonusMultiplier;

                // Additives (integer buffs truncate any fractional value)
                int amountAdd = coreStats.Amount + (int)bAmount + spell.LocalAmountBonus;
                int bounceAdd = coreStats.Bounce + (int)bBounce + spell.LocalBounceBonus;
                int pierceAdd = coreStats.Pierce + (int)bPierce + spell.LocalPierceBonus;
                int splitCountAdd = spell.LocalSplitCount;
                int splitDepthAdd = spell.LocalSplitDepth;
                float explosionSizeAdd = spell.LocalExplosionSizeBonus;

                // 3) Tags used by the RequiredTags-gated SpellStatUpgrade loop below. Bouncing/Piercing are DERIVED from the
                // active state (counter > 0 AND allowed), never read from the authored base / AddedTags. The pre-loop count
                // (base + global + buff + local) is used here so a Bouncing-gated BounceCount modifier is not self-referential.
                ESpellCapability allowed = baseSpellData.AllowedCapabilities;
                const ESpellTag CapabilityBitsMask = ESpellTag.Bouncing | ESpellTag.Piercing;
                ESpellTag derivedCapabilityBits = ESpellTag.None;
                if ((allowed & ESpellCapability.Bounce) != 0 && baseSpellData.Bounces + bounceAdd > 0)
                    derivedCapabilityBits |= ESpellTag.Bouncing;
                if ((allowed & ESpellCapability.Pierce) != 0 && baseSpellData.Pierces + pierceAdd > 0)
                    derivedCapabilityBits |= ESpellTag.Piercing;

                // Explosive is DERIVED from the active Explode capability (entry present AND granted), like
                // Bouncing/Piercing — computed before the SpellStatUpgrade loop so "all Explosive: +x %" sees it.
                // The authored base Explosive bit is NOT masked (Q14: Boss_Fireball keeps its explosion shake).
                bool explodeActive = (allowed & ESpellCapability.Explode) != 0
                                     && (spell.GrantedCapabilities & ESpellCapability.Explode) != 0;
                ESpellTag derivedExplosive = explodeActive ? ESpellTag.Explosive : ESpellTag.None;

                ESpellTag currentTags = (baseSpellData.Tag & ~(StatusBitsMask | CapabilityBitsMask))
                                        | (spell.AddedTags & ~(StatusBitsMask | CapabilityBitsMask | ESpellTag.Explosive))
                                        | derivedStatusBits | derivedCapabilityBits | derivedExplosive;

                // Crit
                float critChanceAdd = coreStats.CritChance + bCritChance + spell.LocalCritChanceBonusPercent;
                float critDmgAdd = 1f + coreStats.CritDamage + bCritDamage + spell.LocalCritDamageBonus;

                // Life steal proc chance (global + temp buff + per-spell), clamped to 0..1 below
                float lifeStealChanceAdd = coreStats.LifeStealChance + bLifeSteal + spell.LocalLifeStealChanceBonus;

                // Per-spell status-effect magnitude bonus (opt-in, only exists if a concrete upgrade creates it):
                // specific-spell upgrades (local) + tagged upgrades/amulets (loop below).
                float burnMagnitudeBonus = spell.LocalBurnMagnitudeBonus;
                float slowMagnitudeBonus = spell.LocalSlowMagnitudeBonus;

                // Spell modifier buffer (tagged upgrades + amulets).
                // All modifiers are additive: mult += Value for every multiplier stat, integer stats and
                // CooldownReduction included.
                for (int j = 0; j < spellStatUpgrades.Length; j++)
                {
                    var mod = spellStatUpgrades[j];

                    if ((currentTags & mod.RequiredTags) != 0)
                    {
                        switch (mod.SpellStat)
                        {
                            case ESpellStat.Damage: dmgMult += mod.Value; break;
                            case ESpellStat.Size: sizeMult += mod.Value; break;
                            case ESpellStat.Speed: speedMult += mod.Value; break;
                            case ESpellStat.Range: rangeMult += mod.Value; break;
                            case ESpellStat.Duration: durationMult += mod.Value; break;
                            case ESpellStat.TickRate: tickRateMult += mod.Value; break;

                            case ESpellStat.CooldownReduction:
                                cdReductionMult += mod.Value;
                                break;

                            case ESpellStat.Amount:
                                amountAdd += (int)mod.Value;
                                break;

                            case ESpellStat.BounceCount:
                                bounceAdd += (int)mod.Value;
                                break;

                            case ESpellStat.PierceCount:
                                pierceAdd += (int)mod.Value;
                                break;

                            case ESpellStat.CritChance:
                                critChanceAdd += mod.Value;
                                break;

                            case ESpellStat.CritDamage:
                                critDmgAdd += mod.Value;
                                break;

                            case ESpellStat.LifeStealChance:
                                lifeStealChanceAdd += mod.Value;
                                break;

                            case ESpellStat.BurnMagnitude:
                                burnMagnitudeBonus += mod.Value;
                                break;

                            case ESpellStat.SlowMagnitude:
                                slowMagnitudeBonus += mod.Value;
                                break;

                            case ESpellStat.ExplosionSize:
                                explosionSizeAdd += mod.Value;
                                break;

                            case ESpellStat.SplitCount:
                                splitCountAdd += (int)mod.Value;
                                break;

                            case ESpellStat.SplitDepth:
                                splitDepthAdd += (int)mod.Value;
                                break;
                        }
                    }
                }

                // Final values cache
                spell.FinalDamage = baseSpellData.BaseDamage * dmgMult;

                spell.FinalSize = math.max(0.1f, baseSpellData.BaseSize * sizeMult);
                spell.FinalSpeed = baseSpellData.BaseSpeed * speedMult;
                spell.FinalDuration = math.max(0.1f, baseSpellData.Lifetime * durationMult);

                // TickRate is an interval (AreaAttackSystem): a +30% bonus means ticks 30% more often → interval / 1.3.
                spell.FinalTickRate = math.max(0.3f, baseSpellData.TickRate / math.max(0.01f, tickRateMult));

                // if passive/aura spell, cooldown is 0, otherwise apply multiplier
                if (baseSpellData.BaseCooldown <= 0)
                    spell.FinalCooldown = 0f;
                else
                    spell.FinalCooldown = math.max(0.1f, baseSpellData.BaseCooldown * (1f - cdReductionMult));

                spell.FinalAmount = math.max(1, baseSpellData.BaseAmount + amountAdd);
                spell.FinalBounces = baseSpellData.Bounces + bounceAdd;
                spell.FinalPierces = baseSpellData.Pierces + pierceAdd;
                spell.FinalSplitCount = math.max(0, baseSpellData.BaseSplitCount + splitCountAdd);
                spell.FinalSplitDepth = math.max(0, baseSpellData.BaseSplitDepth + splitDepthAdd);
                spell.FinalExplosionSize = math.max(-0.9f, explosionSizeAdd);

                // Melee/CaC spells extend their cast range with size upgrades.
                float effectiveRangeMult = baseSpellData.SizeScalesRange
                    ? rangeMult * sizeMult
                    : rangeMult;
                spell.FinalRange = math.max(1f, baseSpellData.BaseCastRange * effectiveRangeMult);
                spell.FinalBounceRange = math.max(1, baseSpellData.BounceRange * bounceRangeMult);

                spell.FinalCritChance = math.clamp(critChanceAdd, 0f, 1f);
                spell.FinalCritDamageMultiplier = math.max(1f, critDmgAdd);

                spell.FinalLifeStealChance = math.clamp(lifeStealChanceAdd, 0f, 1f);

                spell.FinalBurnMagnitudeBonus = burnMagnitudeBonus;
                spell.FinalSlowMagnitudeBonus = slowMagnitudeBonus;

                spell.FinalEffects = effectiveEffects;
                // Definitive Bouncing/Piercing derivation from the FINAL counters (after the gated modifier loop).
                ESpellTag finalTags = currentTags & ~CapabilityBitsMask;
                if (spell.FinalBounces > 0 && (allowed & ESpellCapability.Bounce) != 0) finalTags |= ESpellTag.Bouncing;
                if (spell.FinalPierces > 0 && (allowed & ESpellCapability.Pierce) != 0) finalTags |= ESpellTag.Piercing;
                spell.FinalTags = finalTags;

                // Save
                activeSpells[i] = spell;
            }

            // Clear
            ECB.RemoveComponent<SpellStatsCalculationRequest>(chunkIndex, entity);
        }
    }

    [BurstCompile]
    private partial struct UpdateTickDamageSpellsJob : IJobEntity
    {
        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;

        private void Execute(ref AreaAttack area, ref LocalTransform transform, in SpellSource spellSource)
        {
            // Only OverTime zones are continuously refreshed; Burst zones are set once at cast.
            if (area.Cadence != EZoneCadence.OverTime)
                return;

            if (!ActiveSpellLookup.TryGetBuffer(spellSource.CasterEntity, out var activeSpells))
                return;

            ActiveSpell activeSpell = default;
            var found = false;

            for (var i = 0; i < activeSpells.Length; i++)
            {
                if (activeSpells[i].DatabaseIndex == spellSource.DatabaseIndex)
                {
                    activeSpell = activeSpells[i];
                    found = true;
                    break;
                }
            }

            if (!found)
                return;

            area.Damage = activeSpell.FinalDamage;
            var baseRadius = area.PrefabRadius > 0f ? area.PrefabRadius : 1f;
            area.RadiusStart = activeSpell.FinalSize * baseRadius;
            area.RadiusEnd = area.RadiusStart;
            area.TickRate = activeSpell.FinalTickRate;
            area.Tags |= activeSpell.AddedTags;
            area.CritChance = activeSpell.FinalCritChance;
            area.CritMultiplier = activeSpell.FinalCritDamageMultiplier;

            transform.Scale = activeSpell.FinalSize;
        }
    }
}