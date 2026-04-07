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
    private ComponentLookup<DamageOnTick> _damageOnTickLookup;
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<OrbitMovement> _orbitLookup;

    private BufferLookup<ActiveSpell> _activeSpellLookup;
    private BufferLookup<Child> _childLookup;
    private BufferLookup<SpellModifier> _spellModifierLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<CoreStats>();
        state.RequireForUpdate<SpellsDatabase>();

        _calculationRequestQuery = SystemAPI
            .QueryBuilder()
            .WithAll<SpellStatsCalculationRequest, CoreStats, ActiveSpell, SpellModifier>()
            .Build();

        _activeTickDamageSpellQuery = SystemAPI.QueryBuilder()
            .WithAllRW<DamageOnTick>()
            .WithAllRW<LocalTransform>()
            .WithAll<SpellSource>()
            .Build();

        _subSpellsSpawnerQuery = SystemAPI.QueryBuilder()
            .WithAllRW<SubSpellsSpawner>()
            .WithAll<SpellSource>()
            .Build();

        _statsLookup = state.GetComponentLookup<CoreStats>(true);
        _damageLookup = state.GetComponentLookup<DamageOnContact>(true);
        _damageOnTickLookup = state.GetComponentLookup<DamageOnTick>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _orbitLookup = state.GetComponentLookup<OrbitMovement>(true);

        _activeSpellLookup = state.GetBufferLookup<ActiveSpell>(true);
        _childLookup = state.GetBufferLookup<Child>(true);
        _spellModifierLookup = state.GetBufferLookup<SpellModifier>(true);
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
        _damageOnTickLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _orbitLookup.Update(ref state);
        _childLookup.Update(ref state);
        _spellModifierLookup.Update(ref state);

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
            DamageOnTickLookup = _damageOnTickLookup,
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
            in DynamicBuffer<SpellModifier> spellModifiers)
        {
            ref var blobSpells = ref SpellsDatabaseRef.Value.Spells;

            for (int i = 0; i < activeSpells.Length; i++)
            {
                var spell = activeSpells[i];
                ref var baseSpellData = ref blobSpells[spell.DatabaseIndex];

                ESpellTag currentTags = baseSpellData.Tag | spell.AddedTags;

                // Multipliers
                // Total = Base * ( 1 + Global(Player) + Local(Spell))
                float dmgMult = coreStats.GlobalDamageMultiplier + spell.LocalDamageBonusMultiplier;
                float areaMult = coreStats.GlobalSpellAreaMultiplier + spell.LocalAreaBonusMultiplier;
                float sizeMult = coreStats.GlobalSpellSizeMultiplier + spell.LocalSizeBonusMultiplier;
                float speedMult = coreStats.GlobalSpellSpeedMultiplier + spell.LocalSpeedBonusPercent;
                float durationMult = coreStats.GlobalSpellDurationMultiplier + spell.LocalSpellDurationBonusMultiplier;
                float rangeMult = coreStats.GlobalCastRangeMultiplier + spell.LocalRangeBonusMultiplier;
                float tickRateMult = 1f + spell.LocalTickRateBonusMultiplier;

                float bounceRangeMult =
                    (1 + spell.LocalBounceRangeBonusMultiplier); 
                // todo add global bounce range multiplier if needed

                float cdReductionMult = coreStats.GlobalCooldownReductionMultiplier +
                                        spell.LocalCooldownReducBonusMultiplier;

                // Additives
                int amountAdd = coreStats.GlobalAmountBonus + spell.LocalAmountBonus;
                int bounceAdd = coreStats.GlobalBounceBonus + spell.LocalBounceBonus;
                int pierceAdd = coreStats.GlobalPierceBonus + spell.LocalPierceBonus;

                // Crit
                float critChanceAdd = coreStats.CritChance + spell.LocalCritChanceBonusPercent;
                float critDmgAdd = coreStats.CritDamageMultiplier + spell.LocalCritDamageBonus;

                // Spell modifier buffer
                for (int j = 0; j < spellModifiers.Length; j++)
                {
                    var mod = spellModifiers[j];

                    if ((currentTags & mod.RequiredTags) != 0)
                    {
                        switch (mod.SpellStat)
                        {
                            case ESpellStat.Damage:
                                if (mod.Strategy == EModiferStrategy.Flat) dmgMult += mod.Value;
                                else dmgMult *= mod.Value;
                                break;

                            case ESpellStat.AreaOfEffect:
                                if (mod.Strategy == EModiferStrategy.Flat) areaMult += mod.Value;
                                else areaMult *= (1f + mod.Value);
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
                        }
                    }
                }

                // Final values cache
                spell.FinalDamage = baseSpellData.BaseDamage * dmgMult;

                spell.FinalArea = math.max(0.1f, baseSpellData.BaseAreaOfEffect * areaMult);
                spell.FinalSize = math.max(0.1f, baseSpellData.BaseSize * sizeMult);
                spell.FinalSpeed = baseSpellData.BaseSpeed * speedMult;
                spell.FinalDuration = math.max(0.1f, baseSpellData.Lifetime * durationMult);

                spell.FinalTickRate = math.max(0.1f, baseSpellData.TickRate * tickRateMult);

                // if passive/aura spell, cooldown is 0, otherwise apply multiplier
                if (baseSpellData.BaseCooldown <= 0)
                    spell.FinalCooldown = 0f;
                else
                    spell.FinalCooldown = math.max(0.1f, baseSpellData.BaseCooldown * (1 - cdReductionMult));

                spell.FinalAmount = math.max(1, baseSpellData.BaseAmount + amountAdd);
                spell.FinalBounces = baseSpellData.Bounces + bounceAdd;
                spell.FinalPierces = baseSpellData.Pierces + pierceAdd;

                spell.FinalRange = math.max(1f, baseSpellData.BaseCastRange * rangeMult);
                spell.FinalBounceRange = math.max(1, baseSpellData.BounceRange * bounceRangeMult);

                spell.FinalCritChance = math.clamp(critChanceAdd, 0f, 1f);
                spell.FinalCritDamageMultiplier = math.max(1f, critDmgAdd);

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

        public void Execute(ref DamageOnTick damageOnTick, ref LocalTransform transform, in SpellSource spellSource)
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

            damageOnTick.DamagePerTick = activeSpell.FinalDamage;
            damageOnTick.AreaRadius = activeSpell.FinalArea;
            damageOnTick.TickRate = activeSpell.FinalTickRate;
            damageOnTick.Element |= activeSpell.AddedTags;
            damageOnTick.TickRate = activeSpell.FinalTickRate;
            damageOnTick.TotalCritChance = activeSpell.FinalCritChance;
            damageOnTick.TotalCritMultiplier = activeSpell.FinalCritDamageMultiplier;

            // transform.Scale = activeSpell.FinalArea;
        }
    }

    [BurstCompile]
    private partial struct UpdateSubSpellsJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public ComponentLookup<DamageOnContact> DamageOnContactLookup;
        [ReadOnly] public ComponentLookup<DamageOnTick> DamageOnTickLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<OrbitMovement> OrbitLookup;

        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;
        [ReadOnly] public BufferLookup<Child> ChildLookup;

        public void Execute(
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
                orbit.Radius = activeSpell.FinalArea;
                if (math.lengthsq(orbit.RelativeOffset) > 0.001f)
                    orbit.RelativeOffset = math.normalize(orbit.RelativeOffset) * activeSpell.FinalArea;
                else
                    orbit.RelativeOffset = new float3(0, 0, activeSpell.FinalArea);

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
                        parentTransform.Scale = activeSpell.FinalArea;
                        ECB.SetComponent(chunkIndex, parentEntity, parentTransform);
                    }

                    if (DamageOnContactLookup.HasComponent(child))
                    {
                        var childDmg = DamageOnContactLookup[child];
                        childDmg.Damage = activeSpell.FinalDamage;
                        ECB.SetComponent(chunkIndex, child, childDmg);
                    }

                    if (DamageOnTickLookup.HasComponent(child))
                    {
                        var childDmg = DamageOnTickLookup[child];
                        childDmg.DamagePerTick = activeSpell.FinalDamage;
                        childDmg.AreaRadius = activeSpell.FinalSize;
                        ECB.SetComponent(chunkIndex, child, childDmg);
                    }
                }
            }
        }
    }
}