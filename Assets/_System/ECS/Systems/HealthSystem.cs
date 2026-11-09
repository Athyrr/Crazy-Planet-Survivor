using _System.ECS.Components.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Processes incoming damage from the <see cref="DamageBufferElement"/>, applying elemental
/// resistances and armor reductions before updating the entity's health.
/// </summary>
[UpdateAfter(typeof(CollisionSystem))]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct HealthSystem : ISystem
{
    private ComponentLookup<Player> _playerLookup;
    private ComponentLookup<Enemy> _enemyLookup;
    private ComponentLookup<Boss> _bossLookup;
    private ComponentLookup<Destructible> _destrucibleLookup;
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<DestroyEntityFlag> _destroyFlagLookup;
    private ComponentLookup<ExplodeOnDeath> _explodeOnDeathLookup;
    private ComponentLookup<SoundPlayerTag> _soundPlayaerTagLookup;
    private ComponentLookup<CoreStats> _coreStatsLookup;
    private ComponentLookup<LiveStats> _liveStatsLookup;
    private ComponentLookup<HealthFloor> _healthFloorLookup;
    private ComponentLookup<DamageTally> _damageTallyLookup;
    private ComponentLookup<Invincible> _invincibleLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        _playerLookup = state.GetComponentLookup<Player>(true);
        _enemyLookup = state.GetComponentLookup<Enemy>(true);
        _bossLookup = state.GetComponentLookup<Boss>(true);
        _destrucibleLookup = state.GetComponentLookup<Destructible>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _destroyFlagLookup = state.GetComponentLookup<DestroyEntityFlag>(true);
        _explodeOnDeathLookup = state.GetComponentLookup<ExplodeOnDeath>(true);
        _soundPlayaerTagLookup = state.GetComponentLookup<SoundPlayerTag>(false);
        _coreStatsLookup = state.GetComponentLookup<CoreStats>(true);
        _liveStatsLookup = state.GetComponentLookup<LiveStats>(true);
        _healthFloorLookup = state.GetComponentLookup<HealthFloor>(false);
        _damageTallyLookup = state.GetComponentLookup<DamageTally>(false);
        _invincibleLookup = state.GetComponentLookup<Invincible>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Only process health changes while the game is actively running
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        // Use the EndSimulation ECB to handle entity destruction at the end of the frame
        var ecbSingleton =
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        _playerLookup.Update(ref state);
        _enemyLookup.Update(ref state);
        _bossLookup.Update(ref state);
        _destrucibleLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _destroyFlagLookup.Update(ref state);
        _explodeOnDeathLookup.Update(ref state);

        _soundPlayaerTagLookup.Update(ref state);
        _coreStatsLookup.Update(ref state);
        _liveStatsLookup.Update(ref state);
        _healthFloorLookup.Update(ref state);
        _damageTallyLookup.Update(ref state);
        _invincibleLookup.Update(ref state);

        var soundPlayerEntity = SystemAPI.TryGetSingletonEntity<SoundPlayerTag>(out var spe)
            ? spe
            : Entity.Null;

        // Run-difficulty armor penetration: a fixed flat armor mitigates less as the run advances,
        // so the player must keep raising armor to stay ahead (strong early, useless late).
        EnemyScalingConfig scaleCfg = SystemAPI.TryGetSingleton<EnemyScalingConfig>(out var cfg)
            ? cfg
            : EnemyScalingConfig.Default;

        float playerArmorPen = 0f;
        float enemyDamageMult = 1f;
        // GetSingleton only checks safety, it does not complete RunProgressionJob (heuristics) -> complete it first.
        state.EntityManager.CompleteDependencyBeforeRO<RunProgression>();
        if (SystemAPI.TryGetSingleton<RunProgression>(out var runProg))
        {
            float progTimer = runProg.Timer;
            float progKills = runProg.EnemiesKilledCount;
            // Sandbox `difficulty <n>`: forced difficulty units (inputs only, EnemyScalingConfig untouched).
            if (SystemAPI.TryGetSingleton<SandboxSettings>(out var sandbox))
                SandboxDifficulty.Apply(in sandbox, in scaleCfg, ref progTimer, ref progKills);
            playerArmorPen = scaleCfg.ComputeArmorPenetration(progTimer, progKills);
            enemyDamageMult = scaleCfg.ComputeDamageMult(progTimer, progKills);
        }

        var applyDamageJob = new ApplyDamageJob
        {
            ECB = ecb.AsParallelWriter(),
            DestroyFlagLookup = _destroyFlagLookup,
            PlayerLookup = _playerLookup,
            DestructibleLookup = _destrucibleLookup,
            EnemyLookup = _enemyLookup,
            BossLookup = _bossLookup,
            ExplodeOnDeathLookup = _explodeOnDeathLookup,
            SoundPlayerTagLookup = _soundPlayaerTagLookup,
            SoundPlayerEntity = soundPlayerEntity,
            CoreStatsLookup = _coreStatsLookup,
            LiveStatsLookup = _liveStatsLookup,
            PlayerArmorPen = playerArmorPen,
            EnemyDamageMult = enemyDamageMult,
            MinDamagePerHit = scaleCfg.MinDamagePerHit,
            HealthFloorLookup = _healthFloorLookup,
            DamageTallyLookup = _damageTallyLookup,
            InvincibleLookup = _invincibleLookup,
            ElapsedTime = SystemAPI.Time.ElapsedTime,
        };

        state.Dependency = applyDamageJob.ScheduleParallel(state.Dependency);

        // Heal pass (the sink). Chained AFTER damage via Dependency so a heal can never resurrect
        // an entity zeroed this frame. Each producer already did its magnitude + carryover + feedback;
        // here we only sum, clamp to MaxHealth, and write Health once.
        state.Dependency = new ApplyHealJob().ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// Calculates the total damage for an entity by iterating through its damage buffer
    /// and applying defensive stats.
    /// </summary>
    [BurstCompile]
    private partial struct ApplyDamageJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public ComponentLookup<DestroyEntityFlag> DestroyFlagLookup;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;

        [ReadOnly] public ComponentLookup<Destructible> DestructibleLookup;

        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;

        [ReadOnly] public ComponentLookup<Boss> BossLookup;

        [ReadOnly] public ComponentLookup<ExplodeOnDeath> ExplodeOnDeathLookup;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<SoundPlayerTag> SoundPlayerTagLookup;

        public Entity SoundPlayerEntity;

        [ReadOnly] public ComponentLookup<CoreStats> CoreStatsLookup;
        [ReadOnly] public ComponentLookup<LiveStats> LiveStatsLookup;

        // Flat armor neutralized this frame against the player (run-difficulty scaled).
        public float PlayerArmorPen;

        // Run-difficulty damage multiplier of enemy attacks (same as enemy spells), applied to ExplodeOnDeath.
        public float EnemyDamageMult;

        // Damage floor per hit after armor mitigation.
        public float MinDamagePerHit;

        // Sandbox hooks: written on the iterated entity only (safe in parallel by construction).
        [NativeDisableParallelForRestriction] public ComponentLookup<HealthFloor> HealthFloorLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<DamageTally> DamageTallyLookup;
        [ReadOnly] public ComponentLookup<Invincible> InvincibleLookup;
        public double ElapsedTime;

        // todo Opti here: If  possible use Query instead of lookup

        private void Execute(
            [ChunkIndexInQuery] int index,
            Entity entity,
            ref Health health,
            ref DynamicBuffer<DamageBufferElement> damageBuffer,
            in LocalTransform transform
        )
        {
            // todo use skip condition in query -> Comp + EnabledRefRW

            // Skip entities already marked for destruction
            if (
                DestroyFlagLookup.HasComponent(entity)
                && DestroyFlagLookup.IsComponentEnabled(entity)
            )
                return;

            // Skip if health is already zero or there is no damage to process
            if (health.Value <= 0 || damageBuffer.IsEmpty)
                return;

            // Universal damage-immunity net (god, enemies_invincible): HazardZoneSystem, DashEffectSystem and
            // DashChainDamageSystem write DamageBufferElement directly, without ResolveHit.IsDamageImmune.
            // Immune to damage, not to the hit: hit-side effects already ran upstream, only the damage is dropped.
            if (InvincibleLookup.HasComponent(entity))
            {
                damageBuffer.Clear();
                return;
            }

            var isPlayer = PlayerLookup.HasComponent(entity);

            // Flat armor mitigation from LiveStats (composed = base + permanent + temporary buffs);
            // for the player it is eroded by the run-difficulty armor penetration so a fixed value
            // decays over the run.
            float effectiveArmor = 0f;
            if (LiveStatsLookup.TryGetComponent(entity, out var liveStats))
            {
                float pen = isPlayer ? PlayerArmorPen : 0f;
                effectiveArmor = math.max(0f, liveStats.Armor - pen);
            }

            float totalDamage = 0;
            bool isCritical = false;
            bool isBurn = false;
            EDamageShakeSource shakeSource = EDamageShakeSource.None;

            float frameMaxHit = 0f;
            int frameHits = 0;

            // Process every damage instance stored in the buffer this frame
            for (int i = 0; i < damageBuffer.Length; i++)
            {
                var dbe = damageBuffer[i];
                float damage = dbe.Damage;

                // Flat armor reduction, applied per hit (so it scales with the number of hits,
                // not the frame's summed total).
                if (damage > 0f && effectiveArmor > 0f)
                    damage = math.max(MinDamagePerHit, damage - effectiveArmor);

                damage = math.max(0f, damage);

                if (damage > 0f)
                {
                    frameHits++;
                    frameMaxHit = math.max(frameMaxHit, damage);
                }

                totalDamage += damage;
                isCritical = dbe.IsCritical;
                isBurn = dbe.Tag == ESpellTag.Burn;

                // Keep the strongest source this frame (values ordered by intensity)
                if ((byte)dbe.ShakeSource > (byte)shakeSource)
                    shakeSource = dbe.ShakeSource;
            }

            // Apply the accumulated damage to the health component
            float frameTotal = math.max(0f, totalDamage);

            // Sandbox measurement (dummies, player during ttk): after armor, before the floor and the int cast.
            if (frameHits > 0 && DamageTallyLookup.TryGetRefRW(entity, out var tallyRef))
            {
                ref var tally = ref tallyRef.ValueRW;
                if (tally.Hits == 0)
                    tally.FirstHit = ElapsedTime;
                tally.Total += frameTotal;
                tally.MaxHit = math.max(tally.MaxHit, frameMaxHit);
                tally.Hits += frameHits;
                tally.LastHit = ElapsedTime;
            }

            int newHealth = health.Value - (int)frameTotal;

            // Health floor (dummy, enemies_immortal, Sandbox player): never below the floor, so the death block
            // below is never reached for this entity.
            if (HealthFloorLookup.TryGetRefRW(entity, out var floorRef))
            {
                ref var floor = ref floorRef.ValueRW;
                if (frameHits > 0)
                    floor.LastHitTime = ElapsedTime;

                int floorValue = math.max(1, floor.Floor);
                if (newHealth < floorValue)
                {
                    if (newHealth <= 0)
                    {
                        floor.FloorHits++;
                        floor.PendingFloorHeal = true;
                    }
                    newHealth = floorValue;
                }
            }

            health.Value = newHealth;

            damageBuffer.Clear();

            // Send feedback request
            if (!isPlayer)
            {
                if (
                    SoundPlayerEntity != Entity.Null
                    && SoundPlayerTagLookup.HasComponent(SoundPlayerEntity)
                )
                {
                    var soundTag = SoundPlayerTagLookup[SoundPlayerEntity];
                    soundTag.EnemiesTookDamageSound++;
                    SoundPlayerTagLookup[SoundPlayerEntity] = soundTag;
                }

                TriggerDamageVisual(index, ECB, (int)totalDamage, transform, isCritical, isBurn);
            }
            else
            {
                if (
                    SoundPlayerEntity != Entity.Null
                    && SoundPlayerTagLookup.HasComponent(SoundPlayerEntity)
                )
                {
                    var soundTag = SoundPlayerTagLookup[SoundPlayerEntity];
                    soundTag.PlayerTookDamageSound++;
                    SoundPlayerTagLookup[SoundPlayerEntity] = soundTag;
                }

                // Camera shake whenever the player actually takes damage, scaled by the
                // strongest incoming source (boss > elite > explosion > enemy > DoT).
                if (totalDamage > 0 && shakeSource != EDamageShakeSource.None)
                {
                    var shakeReqEntity = ECB.CreateEntity(index);
                    ECB.AddComponent(index, shakeReqEntity, new ShakeFeedbackRequest
                    {
                        Source = shakeSource,
                    });
                }
            }

            // Check for death condition
            if (health.Value <= 0)
            {
                health.Value = 0;

                if (!isPlayer
                    && ExplodeOnDeathLookup.TryGetComponent(entity, out var explosionData)
                    && ExplodeOnDeathLookup.IsComponentEnabled(entity))
                {
                    var explosionRequest = ECB.CreateEntity(0);
                    ECB.AddComponent(0, explosionRequest, new ExplosionRequest()
                    {
                        Position = transform.Position,
                        // Same difficulty scaling as enemy spells (authored base × EnemyDamageMult).
                        Damage = explosionData.Damage * EnemyDamageMult,
                        VfxPrefab = explosionData.VfxPrefab,
                        Tags = explosionData.Tags,
                        TargetLayers = CollisionLayers.Enemy | CollisionLayers.Player | CollisionLayers.Obstacle,
                        DatabaseIndex = -1, // no source: was 0 by default, attributing the damage to spell 0
                        Damager = Entity.Null,
                    });
                }

                ECB.SetComponentEnabled<DestroyEntityFlag>(index, entity, true);

                if (isPlayer)
                {
                    var endRunReqEntity = ECB.CreateEntity(0);
                    ECB.AddComponent(
                        index,
                        endRunReqEntity,
                        new EndRunRequest() { State = EEndRunState.Death }
                    );
                }
                else if (EnemyLookup.HasComponent(entity))
                {
                    var killedEventEntity = ECB.CreateEntity(index);
                    ECB.AddComponent(
                        index,
                        killedEventEntity,
                        new EnemyKilledEvent { WaveIndex = EnemyLookup[entity].WaveIndex }
                    );

                    // Defeating the planet's final boss is the only way to win a run.
                    if (
                        BossLookup.TryGetComponent(entity, out var boss)
                        && boss.Kind == EBossKind.FinalBoss
                    )
                    {
                        var winEntity = ECB.CreateEntity(index);
                        ECB.AddComponent(
                            index,
                            winEntity,
                            new EndRunRequest { State = EEndRunState.Success }
                        );
                    }
                }
                else if (DestructibleLookup.HasComponent(entity))
                {
                    var killedEventEntity = ECB.CreateEntity(index);
                    ECB.AddComponent(index, killedEventEntity, new EntityKilledEvent { });
                }
            }
        }

        private void TriggerDamageVisual(
            int key,
            EntityCommandBuffer.ParallelWriter ecb,
            int amount,
            LocalTransform transform,
            bool isCritical = false,
            bool isBurn = false
        )
        {
            Entity req = ecb.CreateEntity(key);
            ecb.AddComponent(
                key,
                req,
                new DamageFeedbackRequest
                {
                    Amount = amount,
                    Transform = transform,
                    IsCritical = isCritical,
                    IsBurn = isBurn,
                }
            );
        }
    }

    /// <summary>
    /// Heal pass (the sink). Sums every producer's <see cref="HealBufferElement"/>, clamps to
    /// MaxHealth and performs the single write to <see cref="Health"/>. Scheduled after
    /// <see cref="ApplyDamageJob"/> so a heal cannot revive an entity killed this frame (its
    /// <c>Value</c> is already 0). No feedback here — each producer emits its own green number
    /// (the per-source "fountain").
    /// </summary>
    [BurstCompile]
    private partial struct ApplyHealJob : IJobEntity
    {
        private void Execute(
            ref Health health,
            ref DynamicBuffer<HealBufferElement> healBuffer,
            in CoreStats stats)
        {
            if (healBuffer.IsEmpty)
                return;

            // Killed this frame (damage pass zeroed Value) or already dead → drop queued heals.
            if (health.Value <= 0)
            {
                healBuffer.Clear();
                return;
            }

            int total = 0;
            for (int i = 0; i < healBuffer.Length; i++)
                total += healBuffer[i].Amount;
            healBuffer.Clear();

            if (total <= 0)
                return;

            health.Value = math.min(health.Value + total, (int)stats.MaxHealth);
        }
    }
}

public struct EnemyKilledEvent : IComponentData
{
    public int WaveIndex;
}

// todo impl this and add enemyKilledEvent override EntityKill logic
public struct EntityKilledEvent : IComponentData
{
}