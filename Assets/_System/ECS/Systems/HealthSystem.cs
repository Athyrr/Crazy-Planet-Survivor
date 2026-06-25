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

        var _soundPlayerEntity = SystemAPI.TryGetSingletonEntity<SoundPlayerTag>(out var spe)
            ? spe
            : Entity.Null;

        // Run-difficulty armor penetration: a fixed flat armor mitigates less as the run advances,
        // so the player must keep raising armor to stay ahead (strong early, useless late).
        EnemyScalingConfig scaleCfg = SystemAPI.TryGetSingleton<EnemyScalingConfig>(out var cfg)
            ? cfg
            : EnemyScalingConfig.Default;

        float playerArmorPen = 0f;
        if (SystemAPI.TryGetSingleton<RunProgression>(out var runProg))
            playerArmorPen = scaleCfg.ComputeArmorPenetration(runProg.Timer, runProg.EnemiesKilledCount);

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
            SoundPlayerEntity = _soundPlayerEntity,
            CoreStatsLookup = _coreStatsLookup,
            PlayerArmorPen = playerArmorPen,
            MinDamagePerHit = scaleCfg.MinDamagePerHit,
        };

        state.Dependency = applyDamageJob.ScheduleParallel(state.Dependency);
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

        // Flat armor neutralized this frame against the player (run-difficulty scaled).
        public float PlayerArmorPen;

        // Damage floor per hit after armor mitigation.
        public float MinDamagePerHit;

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

            var isPlayer = PlayerLookup.HasComponent(entity);

            // Flat armor mitigation. Total armor = BaseArmor + Armor; for the player it is eroded
            // by the run-difficulty armor penetration so a fixed armor value decays over the run.
            float effectiveArmor = 0f;
            if (CoreStatsLookup.TryGetComponent(entity, out var coreStats))
            {
                float totalArmor = coreStats.BaseArmor + coreStats.Armor;
                float pen = isPlayer ? PlayerArmorPen : 0f;
                effectiveArmor = math.max(0f, totalArmor - pen);
            }

            float totalDamage = 0;
            bool isCritical = false;
            bool isBurn = false;
            EDamageShakeSource shakeSource = EDamageShakeSource.None;

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

                totalDamage += damage;
                isCritical = dbe.IsCritical;
                isBurn = dbe.Tag == ESpellTag.Burn;

                // Keep the strongest source this frame (values ordered by intensity)
                if ((byte)dbe.ShakeSource > (byte)shakeSource)
                    shakeSource = dbe.ShakeSource;
            }

            // Apply the accumulated damage to the health component
            health.Value -= (int)math.max(0, totalDamage);

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

                if (
                    !isPlayer && ExplodeOnDeathLookup.TryGetComponent(entity, out var explosionData)
                )
                {
                    var explosionRequest = ECB.CreateEntity(0);

                    ECB.AddComponent(
                        0,
                        explosionRequest,
                        new ExplosionRequest()
                        {
                            Position = transform.Position,
                            Damage = explosionData.Damage,
                            VfxPrefab = explosionData.VfxPrefab,
                            IsCritical = false,
                            Tags = explosionData.Tags,
                            TargetLayers =
                                CollisionLayers.Enemy
                                | CollisionLayers.Player
                                | CollisionLayers.Obstacle,
                        }
                    );
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
}

public struct EnemyKilledEvent : IComponentData
{
    public int WaveIndex;
}

// todo impl this and add enemyKilledEvent override EntityKill logic
public struct EntityKilledEvent : IComponentData
{
}