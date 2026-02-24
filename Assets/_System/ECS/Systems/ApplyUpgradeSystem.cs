using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct ApplyUpgradeSystem : ISystem
{
    private BufferLookup<ActiveSpell> _activeSpellsBufferLookup;

    private EntityQuery _subSpellsSpawnerQuery;

    private ComponentLookup<Stats> _statsLookup;
    private ComponentLookup<DamageOnContact> _damageLookup;
    private ComponentLookup<DamageOnTick> _damageOnTickLookup;
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<OrbitMovement> _orbitLookup;
    private BufferLookup<Child> _childLookup;


    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<UpgradesDatabase>();
        state.RequireForUpdate<ApplyUpgradeRequest>();

        _subSpellsSpawnerQuery = new EntityQueryBuilder(Allocator.Temp).WithAllRW<SubSpellsSpawner>()
            .WithAll<SubSpellRoot>().Build(ref state);

        _activeSpellsBufferLookup = SystemAPI.GetBufferLookup<ActiveSpell>(false);

        _statsLookup = state.GetComponentLookup<Stats>(true);
        _damageLookup = state.GetComponentLookup<DamageOnContact>(true);
        _damageOnTickLookup = state.GetComponentLookup<DamageOnTick>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _orbitLookup = state.GetComponentLookup<OrbitMovement>(true);
        _childLookup = state.GetBufferLookup<Child>(true);

        //_subSpellsSpawnerQuery = state.GetEntityQuery(ComponentType.ReadWrite<SubSpellsSpawner>(), ComponentType.ReadOnly<SpellLink>());

        // var types = new NativeArray<ComponentType>(2, Allocator.Temp);
        //types[0] = ComponentType.ReadWrite<SubSpellsSpawner>();
        //types[1] = ComponentType.ReadOnly<SpellLink>();
        //_subSpellsSpawnerQuery = state.GetEntityQuery(types);
        //types.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var gameStateEntity = SystemAPI.GetSingletonEntity<GameState>();

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecbForUpgrades = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var ecbForSubSpells = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();

        var upgradesDatabaseEntity = SystemAPI.GetSingletonEntity<UpgradesDatabase>();
        var upgradesDatabase = SystemAPI.GetComponent<UpgradesDatabase>(upgradesDatabaseEntity);

        var spellsDatabase = SystemAPI.GetSingleton<SpellsDatabase>();

        _activeSpellsBufferLookup.Update(ref state);
        _statsLookup.Update(ref state);
        _damageLookup.Update(ref state);
        _damageOnTickLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _orbitLookup.Update(ref state);
        _childLookup.Update(ref state);


        var applyUpgradeJob = new ApplyUpgradeJob()
        {
            ECB = ecbForUpgrades.AsParallelWriter(),
            GameStateEntity = gameStateEntity,

            PlayerEntity = playerEntity,
            UpgradesDatabaseRef = upgradesDatabase.Blobs,
            SpellsDatabaseRef = spellsDatabase.Blobs,

            ActiveSpellLookup = _activeSpellsBufferLookup,
        };
        JobHandle upgradeHandle = applyUpgradeJob.ScheduleParallel(state.Dependency);

        var updateSubSpellsJob = new UpdateSubSpellsJob
        {
            ECB = ecbForSubSpells.AsParallelWriter(),
            ActiveSpellLookup = _activeSpellsBufferLookup,
            SpellsDatabaseRef = spellsDatabase.Blobs,

            StatsLookup = _statsLookup,
            DamageLookup = _damageLookup,
            DamageOnTickLookup = _damageOnTickLookup,
            TransformLookup = _transformLookup,
            OrbitLookup = _orbitLookup,
            ChildLookup = _childLookup
        };

        state.Dependency = updateSubSpellsJob.ScheduleParallel(_subSpellsSpawnerQuery, upgradeHandle);
    }

    /// <summary>
    /// Updates sub entities spells (ex Fire orbs) after an upgrade.
    /// </summary>
    [BurstCompile]
    private partial struct UpdateSubSpellsJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellsDatabaseRef;

        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
        [ReadOnly] public ComponentLookup<DamageOnContact> DamageLookup;
        [ReadOnly] public ComponentLookup<DamageOnTick> DamageOnTickLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<OrbitMovement> OrbitLookup;
        [ReadOnly] public BufferLookup<Child> ChildLookup;


        public void Execute(
            [ChunkIndexInQuery] int index,
            Entity parentEntity,
            ref SubSpellsSpawner spawner,
            in SubSpellRoot spellRoot)
        {
            if (!ActiveSpellLookup.TryGetBuffer(spellRoot.CasterEntity, out var activeSpells))
                return;

            if (!StatsLookup.TryGetComponent(spellRoot.CasterEntity, out var stats))
                return;

            ActiveSpell currentSpellData = default;
            bool found = false;

            for (int i = 0; i < activeSpells.Length; i++)
            {
                if (activeSpells[i].DatabaseIndex == spellRoot.DatabaseIndex)
                {
                    currentSpellData = activeSpells[i];
                    found = true;
                    break;
                }
            }

            if (!found)
                return;

            ref var baseSpellData = ref SpellsDatabaseRef.Value.Spells[spellRoot.DatabaseIndex];
            int totalAmount = baseSpellData.SubSpellsCount + currentSpellData.BonusAmount;

            //  Define the desired number of children to allow the sub spells system to update the spell
            if (spawner.DesiredSubSpellsCount != totalAmount)
            {
                spawner.DesiredSubSpellsCount = totalAmount;
                spawner.IsDirty = true;
            }

            float finalDamage = (baseSpellData.BaseDamage + stats.Damage) * currentSpellData.DamageMultiplier;
            float finalTickDamage =
                (baseSpellData.BaseDamagePerTick + stats.Damage) * currentSpellData.DamageMultiplier;
            float finalArea = baseSpellData.BaseEffectArea * math.max(1f, stats.EffectAreaRadiusMult) *
                              currentSpellData.AreaMultiplier;
            float finalSpeed = baseSpellData.BaseSpeed * math.max(1f, stats.ProjectileSpeedMultiplier) *
                               currentSpellData.SpeedMultiplier;

            if (DamageLookup.HasComponent(parentEntity))
            {
                var dmg = DamageLookup[parentEntity];
                dmg.Damage = finalDamage; 
                dmg.AreaRadius = finalArea; 
                ECB.SetComponent(index, parentEntity, dmg);

                if (ChildLookup.HasBuffer(parentEntity))
                {
                    var children = ChildLookup[parentEntity];
                    for (int i = 0; i < children.Length; i++)
                    {
                        var child = children[i].Value;
                        if (DamageLookup.HasComponent(child))
                        {
                            var childDmg = DamageLookup[child];
                            childDmg.Damage = finalDamage;
                            childDmg.AreaRadius = finalArea;
                            ECB.SetComponent(index, child, childDmg);
                        }
                    }
                }
            }

            if (DamageOnTickLookup.HasComponent(parentEntity))
            {
                var dmgTick = DamageOnTickLookup[parentEntity];
                dmgTick.DamagePerTick = finalTickDamage;
                dmgTick.AreaRadius = finalArea;
                ECB.SetComponent(index, parentEntity, dmgTick);

                if (ChildLookup.HasBuffer(parentEntity))
                {
                    var children = ChildLookup[parentEntity];
                    for (int i = 0; i < children.Length; i++)
                    {
                        var child = children[i].Value;
                        if (DamageOnTickLookup.HasComponent(child))
                        {
                            var childDmgTick = DamageOnTickLookup[child];
                            childDmgTick.DamagePerTick = finalTickDamage;
                            childDmgTick.AreaRadius = finalArea;
                            ECB.SetComponent(index, child, childDmgTick);
                        }
                    }
                }
            }

            // Scale parent = scale children
            if (TransformLookup.HasComponent(parentEntity))
            {
                var parentTransform = TransformLookup[parentEntity];
                parentTransform.Scale = finalArea;
                ECB.SetComponent(index, parentEntity, parentTransform);
            }

            if (OrbitLookup.HasComponent(parentEntity))
            {
                var orbit = OrbitLookup[parentEntity];
                orbit.AngularSpeed = finalSpeed;
                float orbitRadius = math.length(baseSpellData.BaseSpawnOffset) * currentSpellData.AreaMultiplier;
                orbit.Radius = orbitRadius;
                orbit.RelativeOffset = new float3(0, 0, orbitRadius);

                ECB.SetComponent(index, parentEntity, orbit);
            }
        }
    }

    [BurstCompile]
    private partial struct ApplyUpgradeJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity GameStateEntity;

        [ReadOnly] public Entity PlayerEntity;

        [ReadOnly] public BlobAssetReference<UpgradeBlobs> UpgradesDatabaseRef;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellsDatabaseRef;

        [NativeDisableParallelForRestriction] public BufferLookup<ActiveSpell> ActiveSpellLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity requestEntity, in ApplyUpgradeRequest request)
        {
            int upgradeIndex = request.DatabaseIndex;
            ref var upgradeData = ref UpgradesDatabaseRef.Value.Upgrades[upgradeIndex];

            switch (upgradeData.UpgradeType)
            {
                case EUpgradeType.Stat:
                    var statModifier = new StatModifier()
                    {
                        StatID = upgradeData.CharacterStat,
                        Strategy = upgradeData.ModifierStrategy,
                        Value = upgradeData.Value,
                    };
                    ECB.AppendToBuffer<StatModifier>(chunkIndex, PlayerEntity, statModifier);
                    ECB.AddComponent(chunkIndex, PlayerEntity, new RecalculateStatsRequest());
                    break;

                case EUpgradeType.UnlockSpell:
                    ECB.AppendToBuffer<SpellActivationRequest>(chunkIndex, PlayerEntity, new SpellActivationRequest()
                    {
                        ID = upgradeData.SpellID
                    });
                    break;

                case EUpgradeType.UpgradeSpell:
                    if (ActiveSpellLookup.TryGetBuffer(PlayerEntity, out var activeSpellsBuffer))
                    {
                        ref var spellBlobs = ref SpellsDatabaseRef.Value.Spells;

                        for (int i = 0; i < activeSpellsBuffer.Length; i++)
                        {
                            var activeSpell = activeSpellsBuffer[i];
                            ref var baseData = ref spellBlobs[activeSpell.DatabaseIndex];

                            // Target specific spell
                            bool matchID = upgradeData.SpellID != ESpellID.None && baseData.ID == upgradeData.SpellID;

                            // Target specific tag
                            bool matchTag = upgradeData.SpellID == ESpellID.None &&
                                            (baseData.Tag & upgradeData.SpellTags) > 0;

                            if (matchID || matchTag)
                            {
                                ApplyModification(ref activeSpell, ref upgradeData);
                                activeSpellsBuffer[i] = activeSpell;
                            }
                        }
                    }
                    break;

                default:
                    break;
            }

            // Clear upgrades selection buffer 
            ECB.SetBuffer<UpgradeSelectionBufferElement>(chunkIndex, GameStateEntity);

            // Destroy ApplyUpgradeRequest
            ECB.DestroyEntity(chunkIndex, requestEntity);
        }

        private void ApplyModification(ref ActiveSpell spell, ref UpgradeBlob upgrade)
        {
            switch (upgrade.SpellStat)
            {
                case ESpellStat.Damage:
                    spell.DamageMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.Cooldown:
                    spell.CooldownMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.Speed:
                    spell.SpeedMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.AreaOfEffectSize:
                    spell.AreaMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.Range:
                    spell.RangeMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.Duration:
                    spell.LifetimeMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.Amount:
                    //@todo check for ChildEntitiesSpawner and set dirty and set DesiredChildrenCount +Value
                    spell.BonusAmount += (int)upgrade.Value;
                    break;

                case ESpellStat.TickRate:
                    spell.TickRateMultiplier *= upgrade.Value;
                    break;

                case ESpellStat.BounceCount:
                    spell.BonusBounces += (int)upgrade.Value;
                    break;

                case ESpellStat.PierceCount:
                    spell.BonusPierces += (int)upgrade.Value;
                    break;

                case ESpellStat.CritChance:
                    spell.BonusCritChance += upgrade.Value;
                    break;

                case ESpellStat.CritMultiplier:
                    spell.BonusCritMultiplier += upgrade.Value;
                    break;

                default:
                    break;
            }

            spell.Level++;
        }
    }
}