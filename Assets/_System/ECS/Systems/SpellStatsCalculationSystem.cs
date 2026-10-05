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
    private EntityQuery _subSpellsSpawnerQuery;

    private ComponentLookup<CoreStats> _statsLookup;
    private ComponentLookup<DamageOnContact> _damageLookup;
    private ComponentLookup<AreaAttack> _areaAttackLookup;
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<OrbitMovement> _orbitLookup;

    private BufferLookup<ActiveSpell> _activeSpellLookup;
    private BufferLookup<Child> _childLookup;
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

        _subSpellsSpawnerQuery = SystemAPI.QueryBuilder()
            .WithAllRW<SubSpellsSpawner>()
            .WithAll<SpellSource>()
            .Build();

        _statsLookup = state.GetComponentLookup<CoreStats>(true);
        _damageLookup = state.GetComponentLookup<DamageOnContact>(true);
        _areaAttackLookup = state.GetComponentLookup<AreaAttack>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _orbitLookup = state.GetComponentLookup<OrbitMovement>(true);

        _activeSpellLookup = state.GetBufferLookup<ActiveSpell>(true);
        _childLookup = state.GetBufferLookup<Child>(true);
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
        var ecbSubSpells = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var spellBlobs = SystemAPI.GetSingleton<SpellsDatabase>().Blobs;

        _activeSpellLookup.Update(ref state);
        _statsLookup.Update(ref state);
        _damageLookup.Update(ref state);
        _areaAttackLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _orbitLookup.Update(ref state);
        _childLookup.Update(ref state);
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

        // Update sub spells (eg. Fire orbs)
        var updateSubSpellsJob = new UpdateSubSpellsJob
        {
            ECB = ecbSubSpells.AsParallelWriter(),
            ActiveSpellLookup = _activeSpellLookup,
            // SpellsDatabaseRef = spellBlobs,

            DamageOnContactLookup = _damageLookup,
            AreaAttackLookup = _areaAttackLookup,
            TransformLookup = _transformLookup,
            OrbitLookup = _orbitLookup,
            ChildLookup = _childLookup
        };
        // JobHandle subSpellHandle =
        //     updateSubSpellsJob.ScheduleParallel(_subSpellsSpawnerQuery, calculateSpellStatsJobHandle); 

        JobHandle subSpellHandle =
            updateSubSpellsJob.ScheduleParallel(_subSpellsSpawnerQuery, updateTickDamageSpellJobHandle);

        state.Dependency = subSpellHandle;

        // System ends after both jobs (tick spells and sub spells) are done, but can run in parallel
        // state.Dependency = JobHandle.CombineDependencies(updateTickDamageSpellJobHandle, subSpellHandle);
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

                // 2) Derive Tags' 4 status bits from effectiveEffects — never the other way around. AddedTags is NOT dead
                // weight after this chantier — verified live, not assumed: Upgrade_Spell_Fireball_Explosive.asset:21 and
                // Upgrade_Spell_ShockChain_Explosive.asset:21 (both RequiredTags=8192=ESpellTag.Explosive, SpellID set)
                // still grant a real behavior tag through AddedTags, consumed by SpellCastingSystem.cs's
                // `forceExplode = (totalTags & ESpellTag.Explosive) != 0` (~line 716) — out of this chantier's scope,
                // chantier #4. AddedTags' own copy of the 4 STATUS bits (from a status-granting Upgrade_Spell_* asset's
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
                // todo add explosion dmg + explosion size stats
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

                ESpellTag currentTags = (baseSpellData.Tag & ~(StatusBitsMask | CapabilityBitsMask))
                                        | (spell.AddedTags & ~(StatusBitsMask | CapabilityBitsMask))
                                        | derivedStatusBits | derivedCapabilityBits;

                // Crit
                float critChanceAdd = coreStats.CritChance + bCritChance + spell.LocalCritChanceBonusPercent;
                float critDmgAdd = 1f + coreStats.CritDamage + bCritDamage + spell.LocalCritDamageBonus;

                // Life steal proc chance (global + temp buff + per-spell), clamped to 0..1 below
                float lifeStealChanceAdd = coreStats.LifeStealChance + bLifeSteal + spell.LocalLifeStealChanceBonus;

                // Per-spell status-effect magnitude bonus (opt-in, only exists if a concrete upgrade creates it)
                float burnMagnitudeBonus = 0f;
                float slowMagnitudeBonus = 0f;

                // Spell modifier buffer
                for (int j = 0; j < spellStatUpgrades.Length; j++)
                {
                    var mod = spellStatUpgrades[j];

                    if ((currentTags & mod.RequiredTags) != 0)
                    {
                        switch (mod.SpellStat)
                        {
                            case ESpellStat.Damage:
                                if (mod.Strategy == EModiferStrategy.Flat) dmgMult += mod.Value;
                                else dmgMult *= mod.Value;
                                break;

                            case ESpellStat.Size:
                                if (mod.Strategy == EModiferStrategy.Flat) sizeMult += mod.Value;
                                else sizeMult *= (1f + mod.Value);
                                break;

                            case ESpellStat.Speed:
                                if (mod.Strategy == EModiferStrategy.Flat) speedMult += mod.Value;
                                else speedMult *= (1f + mod.Value);
                                break;

                            case ESpellStat.CooldownReduction:
                                //todo reclaculate cd
                                if (mod.Strategy == EModiferStrategy.Flat) cdReductionMult += mod.Value;
                                // else cdReductionMult *= math.max(0.1f, 1f - mod.Value);
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
                        }
                    }
                }

                // Final values cache
                spell.FinalDamage = baseSpellData.BaseDamage * dmgMult;

                spell.FinalSize = math.max(0.1f, baseSpellData.BaseSize * sizeMult);
                spell.FinalSpeed = baseSpellData.BaseSpeed * speedMult;
                spell.FinalDuration = math.max(0.1f, baseSpellData.Lifetime * durationMult);

                spell.FinalTickRate = math.max(0.3f, baseSpellData.TickRate * tickRateMult);

                // if passive/aura spell, cooldown is 0, otherwise apply multiplier
                if (baseSpellData.BaseCooldown <= 0)
                    spell.FinalCooldown = 0f;
                else
                    spell.FinalCooldown = math.max(0.1f, baseSpellData.BaseCooldown * (1f - cdReductionMult));

                spell.FinalAmount = math.max(1, baseSpellData.BaseAmount + amountAdd);
                spell.FinalBounces = baseSpellData.Bounces + bounceAdd;
                spell.FinalPierces = baseSpellData.Pierces + pierceAdd;

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

    [BurstCompile]
    private partial struct UpdateSubSpellsJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public ComponentLookup<DamageOnContact> DamageOnContactLookup;
        [ReadOnly] public ComponentLookup<AreaAttack> AreaAttackLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<OrbitMovement> OrbitLookup;

        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;
        [ReadOnly] public BufferLookup<Child> ChildLookup;

        private static void AppendUniqueEffects(ref FixedList32Bytes<EffectSpec> list, in FixedList32Bytes<EffectSpec> toAdd)
        {
            for (int i = 0; i < toAdd.Length; i++)
            {
                bool alreadyPresent = false;
                for (int j = 0; j < list.Length; j++)
                {
                    if (list[j].Type == toAdd[i].Type) { alreadyPresent = true; break; }
                }
                if (!alreadyPresent)
                    list.Add(toAdd[i]);
            }
        }

        private void Execute(
            [ChunkIndexInQuery] int chunkIndex,
            Entity parentEntity,
            ref SubSpellsSpawner spawner,
            in SpellSource spellSource)
        {
            if (!ActiveSpellLookup.TryGetBuffer(spellSource.CasterEntity, out var activeSpells))
                return;

            ActiveSpell activeSpell = default;
            bool found = false;

            for (int i = 0; i < activeSpells.Length; i++)
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

            if (spawner.DesiredSubSpellsCount != activeSpell.FinalAmount)
            {
                spawner.DesiredSubSpellsCount = activeSpell.FinalAmount;
                spawner.IsDirty = true;
            }

            if (OrbitLookup.HasComponent(parentEntity))
            {
                var orbit = OrbitLookup[parentEntity];
                orbit.AngularSpeed = activeSpell.FinalSpeed;
                orbit.Radius = activeSpell.FinalRange;
                if (math.lengthsq(orbit.RelativeOffset) > 0.001f)
                    orbit.RelativeOffset = math.normalize(orbit.RelativeOffset) * activeSpell.FinalRange;
                else
                    orbit.RelativeOffset = new float3(0, 0, activeSpell.FinalRange);

                ECB.SetComponent(chunkIndex, parentEntity, orbit);
            }

            if (ChildLookup.HasBuffer(parentEntity))
            {
                var children = ChildLookup[parentEntity];
                for (int i = 0; i < children.Length; i++)
                {
                    var child = children[i].Value;

                    // Scale parent = scale children
                    if (TransformLookup.HasComponent(parentEntity))
                    {
                        var parentTransform = TransformLookup[parentEntity];
                        parentTransform.Scale = activeSpell.FinalRange;
                        ECB.SetComponent(chunkIndex, parentEntity, parentTransform);
                    }

                    if (DamageOnContactLookup.HasComponent(child))
                    {
                        var childDmg = DamageOnContactLookup[child];
                        childDmg.Damage = activeSpell.FinalDamage;
                        childDmg.Tags |= activeSpell.AddedTags;
                        AppendUniqueEffects(ref childDmg.EffectsToApply, activeSpell.AddedEffects);
                        ECB.SetComponent(chunkIndex, child, childDmg);
                    }

                    if (AreaAttackLookup.HasComponent(child))
                    {
                        var childDmg = AreaAttackLookup[child];
                        childDmg.Damage = activeSpell.FinalDamage;
                        float childBaseRadius = childDmg.PrefabRadius > 0f ? childDmg.PrefabRadius : 1f;
                        childDmg.RadiusStart = activeSpell.FinalSize * childBaseRadius;
                        childDmg.RadiusEnd = childDmg.RadiusStart;
                        childDmg.Tags |= activeSpell.AddedTags;
                        AppendUniqueEffects(ref childDmg.EffectsToApply, activeSpell.AddedEffects);
                        ECB.SetComponent(chunkIndex, child, childDmg);
                    }
                }
            }
        }
    }
}