using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Handles one-shot area attack spells (VoidSlash, Shockwave, ShockStrike, etc.).
///
/// Each frame during the active window [ActivationDelay, ActivationDelay + ActiveDuration]:
///   1. Interpolates shape parameters (radius, sweep angle) linearly
///   2. Performs OverlapSphere at the entity's position
///   3. Filters hits by shape (Cone: angle from swept forward, Ring: distance band)
///   4. Deduplicates against HitEntityMemory buffer
///   5. Applies damage via ECB
///   6. Applies active effects (knockback, slow, stun, burn) based on ESpellTag flags
///
/// After the active window, evaluation stops but the entity stays alive
/// for VFX (destroyed separately by LifetimeSystem).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct AreaAttackSystem : ISystem
{
    private ComponentLookup<Destructible> _destructibleLookup;
    private BufferLookup<DamageBufferElement> _damageBufferLookup;
    private ComponentLookup<LocalToWorld> _ltwTransformLookup;
    private ComponentLookup<ActiveKnockback> _knockbackLookup;
    private ComponentLookup<SlowEffect> _slowLookup;
    private ComponentLookup<StunEffect> _stunLookup;
    private ComponentLookup<BurnEffect> _burnLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<AreaAttack>();
        state.RequireForUpdate<ActiveEffectsConfig>();
        state.RequireForUpdate<Player>();

        _destructibleLookup = state.GetComponentLookup<Destructible>(true);
        _damageBufferLookup = state.GetBufferLookup<DamageBufferElement>(true);
        _ltwTransformLookup = state.GetComponentLookup<LocalToWorld>(true);
        _knockbackLookup = state.GetComponentLookup<ActiveKnockback>(true);
        _slowLookup = state.GetComponentLookup<SlowEffect>(true);
        _stunLookup = state.GetComponentLookup<StunEffect>(true);
        _burnLookup = state.GetComponentLookup<BurnEffect>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();
        float3 playerPosition = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO.Position;
        var effectsConfig = SystemAPI.GetSingleton<ActiveEffectsConfig>();

        _destructibleLookup.Update(ref state);
        _damageBufferLookup.Update(ref state);
        _ltwTransformLookup.Update(ref state);
        _knockbackLookup.Update(ref state);
        _slowLookup.Update(ref state);
        _stunLookup.Update(ref state);
        _burnLookup.Update(ref state);

        var job = new AreaAttackJob
        {
            ECB = ecb.AsParallelWriter(),
            DeltaTime = SystemAPI.Time.DeltaTime,
            CollisionWorld = collisionWorld,
            PlayerPosition = playerPosition,
            EffectsConfig = effectsConfig,
            DestructibleLookup = _destructibleLookup,
            DamageBufferLookup = _damageBufferLookup,
            LtwTransformLookup = _ltwTransformLookup,
            KnockbackLookup = _knockbackLookup,
            SlowLookup = _slowLookup,
            StunLookup = _stunLookup,
            BurnLookup = _burnLookup,
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct AreaAttackJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public float3 PlayerPosition;
        [ReadOnly] public ActiveEffectsConfig EffectsConfig;

        [ReadOnly] public ComponentLookup<Destructible> DestructibleLookup;
        [ReadOnly] public BufferLookup<DamageBufferElement> DamageBufferLookup;
        [ReadOnly] public ComponentLookup<LocalToWorld> LtwTransformLookup;
        [ReadOnly] public ComponentLookup<ActiveKnockback> KnockbackLookup;
        [ReadOnly] public ComponentLookup<SlowEffect> SlowLookup;
        [ReadOnly] public ComponentLookup<StunEffect> StunLookup;
        [ReadOnly] public ComponentLookup<BurnEffect> BurnLookup;

        private void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity,
            ref AreaAttack areaAttack, in LocalToWorld localToWorld,
            ref DynamicBuffer<HitEntityMemory> hitMemory)
        {
            // ── Timer ──
            areaAttack.ElapsedTime += DeltaTime;

            // Not yet active
            if (areaAttack.ElapsedTime < areaAttack.ActivationDelay)
                return;

            // Past active window → stop evaluating (entity stays alive for VFX)
            float activeTime = areaAttack.ElapsedTime - areaAttack.ActivationDelay;
            if (activeTime > areaAttack.ActiveDuration)
                return;

            // ── Interpolate shape parameters ──
            float t = areaAttack.ActiveDuration > 0f
                ? math.saturate(activeTime / areaAttack.ActiveDuration)
                : 1f;

            float currentRadius = math.lerp(areaAttack.RadiusStart, areaAttack.RadiusEnd, t);
            float currentSweep = math.lerp(areaAttack.SweepStart, areaAttack.SweepEnd, t);

            // ── OverlapSphere query ──
            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = areaAttack.TargetLayers
            };

            var hits = new NativeList<DistanceHit>(64, Allocator.Temp);
            CollisionWorld.OverlapSphere(localToWorld.Position, currentRadius, ref hits, filter);

            float3 position = localToWorld.Position;
            quaternion rotation = localToWorld.Rotation;

            // Per-entity random seed for crit checks
            var random = Random.CreateFromIndex((uint)(entity.Index + 1));

            for (int i = 0; i < hits.Length; i++)
            {
                Entity hitEntity = hits[i].Entity;
                if (hitEntity == entity || hitEntity == Entity.Null)
                    continue;

                // Must be able to receive damage
                if (!DamageBufferLookup.HasBuffer(hitEntity))
                    continue;

                // Must be alive
                if (DestructibleLookup.HasComponent(hitEntity)
                    && !DestructibleLookup.IsComponentEnabled(hitEntity))
                    continue;

                // Already hit by this attack instance
                if (IsInHitMemory(hitMemory, hitEntity))
                    continue;

                // Shape-specific filtering
                if (!IsInShape(position, rotation, areaAttack, currentRadius, currentSweep, hits[i].Position))
                    continue;

                // Apply damage
                bool isCrit = random.NextFloat(0f, 1f) < areaAttack.CritChance;

                ECB.AppendToBuffer(chunkIndex, hitEntity, new DamageBufferElement
                {
                    Damage = (int)areaAttack.Damage,
                    Tag = areaAttack.Tags,
                    IsCritical = isCrit,
                });

                // Active effects (based on spell tags)

                if ((areaAttack.Tags & ESpellTag.Knockback) != 0)
                {
                    float3 targetPos = LtwTransformLookup[hitEntity].Position;
                    float3 pushDir = targetPos - PlayerPosition;
                    float distSq = math.lengthsq(pushDir);

                    if (distSq > 0.001f)
                        pushDir = math.normalize(pushDir);
                    else
                        pushDir = math.forward(localToWorld.Rotation);

                    var kbData = new ActiveKnockback
                    {
                        Direction = pushDir,
                        InitialForce = EffectsConfig.KnockbackForce,
                        DurationLeft = EffectsConfig.KnockbackDuration,
                        MaxDuration = EffectsConfig.KnockbackDuration,
                    };

                    if (KnockbackLookup.HasComponent(hitEntity))
                        ECB.SetComponent(chunkIndex, hitEntity, kbData);
                    else
                        ECB.AddComponent(chunkIndex, hitEntity, kbData);
                }

                if ((areaAttack.Tags & ESpellTag.Slow) != 0)
                {
                    var slow = new SlowEffect
                    {
                        SpeedReductionMultiplier = EffectsConfig.BaseSlowMultiplier,
                        DurationLeft = EffectsConfig.SlowDuration,
                    };

                    if (SlowLookup.HasComponent(hitEntity))
                    {
                        ECB.SetComponent(chunkIndex, hitEntity, slow);
                        ECB.SetComponentEnabled<SlowEffect>(chunkIndex, hitEntity, true);
                    }
                    else
                    {
                        ECB.AddComponent(chunkIndex, hitEntity, slow);
                    }
                }

                if ((areaAttack.Tags & ESpellTag.Stun) != 0)
                {
                    var stun = new StunEffect
                    {
                        DurationLeft = EffectsConfig.StunDuration,
                    };

                    if (StunLookup.HasComponent(hitEntity))
                    {
                        ECB.SetComponent(chunkIndex, hitEntity, stun);
                        ECB.SetComponentEnabled<StunEffect>(chunkIndex, hitEntity, true);
                    }
                    else
                    {
                        ECB.AddComponent(chunkIndex, hitEntity, stun);
                    }
                }

                if ((areaAttack.Tags & ESpellTag.Burn) != 0)
                {
                    var burn = new BurnEffect
                    {
                        DamageOnTick = EffectsConfig.BurnDamageRatio * areaAttack.Damage,
                        TickRate = EffectsConfig.BurnTickRate,
                        TickTimer = 0f,
                        RemainingTime = EffectsConfig.BurnDuration,
                    };

                    if (BurnLookup.HasComponent(hitEntity))
                    {
                        ECB.SetComponent(chunkIndex, hitEntity, burn);
                        ECB.SetComponentEnabled<BurnEffect>(chunkIndex, hitEntity, true);
                    }
                    else
                    {
                        ECB.AddComponent(chunkIndex, hitEntity, burn);
                    }
                }

                // Record hit in memory (one-shot per enemy guarantee)
                hitMemory.Add(new HitEntityMemory
                {
                    HitEntity = hitEntity,
                    LastHitTime = 0f,
                });
            }

            hits.Dispose();
        }

        /// <summary>Check if the entity was already hit by this attack.</summary>
        private static bool IsInHitMemory(in DynamicBuffer<HitEntityMemory> hitMemory, Entity hitEntity)
        {
            for (int i = 0; i < hitMemory.Length; i++)
            {
                if (hitMemory[i].HitEntity == hitEntity)
                    return true;
            }
            return false;
        }

        /// <summary>Shape-based filtering beyond OverlapSphere.</summary>
        private static bool IsInShape(float3 position, quaternion rotation,
            in AreaAttack areaAttack, float currentRadius, float currentSweep, float3 hitPosition)
        {
            switch (areaAttack.Shape)
            {
                case EAttackAreaShape.Circle:
                    // Already filtered by OverlapSphere radius
                    return true;

                case EAttackAreaShape.Cone:
                {
                    // Sweep the cone center around the forward direction (Y-up rotation)
                    float3 forward = math.forward(rotation);
                    quaternion sweepRot = quaternion.RotateY(currentSweep);
                    float3 coneDir = math.normalize(math.mul(sweepRot, forward));

                    float3 toHit = math.normalize(hitPosition - position);
                    float cosAngle = math.dot(coneDir, toHit);
                    float halfAngleCos = math.cos(areaAttack.HalfAngle);

                    return cosAngle >= halfAngleCos;
                }

                case EAttackAreaShape.Ring:
                {
                    float dist = math.distance(position, hitPosition);
                    float halfThickness = areaAttack.RingThickness * 0.5f;
                    float distFromRing = math.abs(dist - currentRadius);
                    return distFromRing <= halfThickness;
                }

                default:
                    return false;
            }
        }
    }
}
