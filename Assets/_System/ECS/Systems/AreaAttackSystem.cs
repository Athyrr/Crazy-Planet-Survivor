using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Unified area-of-effect delivery system. Merges the former AreaAttackSystem (Burst one-shot) and
/// TickDamageSystem (OverTime aura) into a single system branched on <see cref="AreaAttack.Cadence"/>.
///
///   Burst    — hit each target once over [ActivationDelay, +ActiveDuration]; shape can animate
///              (Expand/Sweep). Dedup via <see cref="HitEntityMemory"/>. (VoidSlash, ShockStrike…)
///   OverTime — re-hit tracked targets every TickRate; enter/exit via <see cref="TickDamageTarget"/>. (FrozenZone…)
///
/// Shape filtering (<see cref="IsInShape"/>) is done here; the *consequences* of a hit (crit roll +
/// multiplier, damage buffer, tag effects, life steal, damage tracking) are delegated to the shared
/// <see cref="ResolveHit"/> helper — the same one the projectile-contact path uses. Both cadences build
/// a <see cref="HitAction"/> and call it, so crit is applied uniformly (this fixes the former "crit
/// gruyère": the Burst path applied a cosmetic crit and the OverTime tick never rolled at all).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct AreaAttackSystem : ISystem
{
    private ComponentLookup<Destructible> _destructibleLookup;
    private BufferLookup<DamageBufferElement> _damageBufferLookup;
    private ComponentLookup<LocalToWorld> _ltwLookup;
    private ComponentLookup<DestroyEntityFlag> _destroyFlagLookup;
    private ComponentLookup<LiveStats> _liveStatsLookup;
    private ComponentLookup<ActiveKnockback> _knockbackLookup;
    private ComponentLookup<SlowEffect> _slowLookup;
    private ComponentLookup<StunEffect> _stunLookup;
    private ComponentLookup<BurnEffect> _burnLookup;
    private ComponentLookup<SpellSource> _spellSourceLookup;
    private ComponentLookup<Boss> _bossLookup;
    private BufferLookup<ActiveSpell> _activeSpellLookup;

    private NativeQueue<SpellDamageEvent> _damageEventsQueue;

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
        _ltwLookup = state.GetComponentLookup<LocalToWorld>(true);
        _destroyFlagLookup = state.GetComponentLookup<DestroyEntityFlag>(true);
        _liveStatsLookup = state.GetComponentLookup<LiveStats>(true);
        _knockbackLookup = state.GetComponentLookup<ActiveKnockback>(true);
        _slowLookup = state.GetComponentLookup<SlowEffect>(true);
        _stunLookup = state.GetComponentLookup<StunEffect>(true);
        _burnLookup = state.GetComponentLookup<BurnEffect>(true);
        _spellSourceLookup = state.GetComponentLookup<SpellSource>(true);
        _bossLookup = state.GetComponentLookup<Boss>(true);
        _activeSpellLookup = state.GetBufferLookup<ActiveSpell>(false);

        _damageEventsQueue = new NativeQueue<SpellDamageEvent>(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_damageEventsQueue.IsCreated)
            _damageEventsQueue.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        // One ECB per job: a system ECB only supports a single producer job, so Burst and OverTime
        // each get their own (both play back at EndSimulation).
        var ecbBurst = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var ecbOverTime = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var effectsConfig = SystemAPI.GetSingleton<ActiveEffectsConfig>();

        var playerEntity = SystemAPI.GetSingletonEntity<Player>();
        float3 playerPosition = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO.Position;
        float lifeStealConversion = SystemAPI.TryGetSingleton<LifeStealConfig>(out var lsCfg)
            ? lsCfg.Conversion
            : 0.075f;

        _destructibleLookup.Update(ref state);
        _damageBufferLookup.Update(ref state);
        _ltwLookup.Update(ref state);
        _destroyFlagLookup.Update(ref state);
        _liveStatsLookup.Update(ref state);
        _knockbackLookup.Update(ref state);
        _slowLookup.Update(ref state);
        _stunLookup.Update(ref state);
        _burnLookup.Update(ref state);
        _spellSourceLookup.Update(ref state);
        _bossLookup.Update(ref state);
        _activeSpellLookup.Update(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;
        uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1;

        // Shared hit-resolution context — the effect config, effect lookups, life-steal data and the
        // damage-tracking queue that ResolveHit needs. Built once, copied into both cadence jobs.
        var resolveContext = new ResolveHitContext
        {
            EffectsConfig = effectsConfig,
            LifeStealConversion = lifeStealConversion,
            PlayerEntity = playerEntity,
            SlowLookup = _slowLookup,
            StunLookup = _stunLookup,
            BurnLookup = _burnLookup,
            KnockbackLookup = _knockbackLookup,
            LtwLookup = _ltwLookup,
            ActiveSpellLookup = _activeSpellLookup,
            DamageEventsWriter = _damageEventsQueue.AsParallelWriter(),
        };

        // Burst zones — hit-once over an active window (matches entities carrying HitEntityMemory).
        var burstJob = new BurstZoneJob
        {
            ECB = ecbBurst.AsParallelWriter(),
            DeltaTime = deltaTime,
            CollisionWorld = collisionWorld,
            PlayerPosition = playerPosition,
            DestructibleLookup = _destructibleLookup,
            DamageBufferLookup = _damageBufferLookup,
            SpellSourceLookup = _spellSourceLookup,
            BossLookup = _bossLookup,
            Resolve = resolveContext,
        };
        JobHandle burstHandle = burstJob.ScheduleParallel(state.Dependency);

        // OverTime zones — re-hit tracked targets on a tick cadence (matches entities carrying TickDamageTarget).
        var overTimeJob = new OverTimeZoneJob
        {
            ECB = ecbOverTime.AsParallelWriter(),
            DeltaTime = deltaTime,
            CollisionWorld = collisionWorld,
            LiveStatsLookup = _liveStatsLookup,
            LtwLookup = _ltwLookup,
            DestroyFlagLookup = _destroyFlagLookup,
            DamageBufferLookup = _damageBufferLookup,
            Resolve = resolveContext,
            Seed = seed,
        };
        JobHandle overTimeHandle = overTimeJob.ScheduleParallel(burstHandle);

        var trackJob = new TrackDamageJob
        {
            DamageEventsQueue = _damageEventsQueue,
            ActiveSpellLookup = _activeSpellLookup,
            PlayerEntity = playerEntity,
        };
        state.Dependency = trackJob.Schedule(overTimeHandle);
    }

    // ── Shared helpers (nested jobs call these directly) ──

    /// <summary>Shape-based filtering beyond the OverlapSphere radius. Circle is fully covered by the sphere.</summary>
    private static bool IsInShape(EAttackAreaShape shape, float3 pos, quaternion rot,
        float radius, float halfAngle, float sweep, float ringThickness, float3 hitPos)
    {
        switch (shape)
        {
            case EAttackAreaShape.Circle:
                return true;

            case EAttackAreaShape.Cone:
            {
                // Sweep around the entity's up axis (Y-local in world = surface normal at cast time,
                // baked in by LookRotationSafe(dir, surfaceNormal) in SpellCastingSystem). Rotating around
                // world Y instead — the previous behavior — tilted the swept arc on a curved planet.
                float3 fwd = math.forward(rot);
                float3 upAxis = math.mul(rot, math.up());
                float3 coneDir = math.normalize(math.mul(quaternion.AxisAngle(upAxis, sweep), fwd));
                float3 toHit = math.normalize(hitPos - pos);
                return math.dot(coneDir, toHit) >= math.cos(halfAngle);
            }

            case EAttackAreaShape.Ring:
            {
                float dist = math.distance(pos, hitPos);
                return math.abs(dist - radius) <= ringThickness * 0.5f;
            }

            default:
                return false;
        }
    }

    private static bool IsInHitMemory(in DynamicBuffer<HitEntityMemory> hitMemory, Entity hitEntity)
    {
        for (int i = 0; i < hitMemory.Length; i++)
            if (hitMemory[i].HitEntity == hitEntity)
                return true;
        return false;
    }

    /// <summary>True if <paramref name="p"/> is within <paramref name="halfWidth"/> of the segment [a,b]
    /// (Capsule shape: a thrust/line of width 2×halfWidth).</summary>
    private static bool CapsuleContains(float3 a, float3 b, float halfWidth, float3 p)
    {
        float3 ab = b - a;
        float abLenSq = math.lengthsq(ab);
        float t = abLenSq > math.EPSILON ? math.saturate(math.dot(p - a, ab) / abLenSq) : 0f;
        float3 closest = a + t * ab;
        return math.distancesq(p, closest) <= halfWidth * halfWidth;
    }

    // ── Burst cadence (was AreaAttackJob) ──
    [BurstCompile]
    private partial struct BurstZoneJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public float3 PlayerPosition;

        [ReadOnly] public ComponentLookup<Destructible> DestructibleLookup;
        [ReadOnly] public BufferLookup<DamageBufferElement> DamageBufferLookup;
        [ReadOnly] public ComponentLookup<SpellSource> SpellSourceLookup;
        [ReadOnly] public ComponentLookup<Boss> BossLookup;

        public ResolveHitContext Resolve;

        private void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity,
            ref AreaAttack area, in LocalToWorld localToWorld, ref DynamicBuffer<HitEntityMemory> hitMemory)
        {
            if (area.Cadence != EZoneCadence.Burst)
                return;

            area.ElapsedTime += DeltaTime;
            if (area.ElapsedTime < area.ActivationDelay)
                return;

            float activeTime = area.ElapsedTime - area.ActivationDelay;
            if (activeTime > area.ActiveDuration)
                return;

            float t = area.ActiveDuration > 0f ? math.saturate(activeTime / area.ActiveDuration) : 1f;
            float currentRadius = math.lerp(area.RadiusStart, area.RadiusEnd, t);
            float currentSweep = math.lerp(area.SweepStart, area.SweepEnd, t);

            quaternion rotation = localToWorld.Rotation;

            // Hitbox anchoring (rotation + scale aware, matches the visual):
            //  • Capsule spans the entity origin → its Offset endpoint (a thrust/line), half-width = RadiusStart.
            //  • Other shapes are centered at the Offset point.
            float3 offsetPoint = math.transform(localToWorld.Value, area.Offset);
            bool isCapsule = area.Shape == EAttackAreaShape.Capsule;
            float3 capsuleA = localToWorld.Position;
            // Progressive thrust: the capsule extends from the entity to its Offset endpoint over the active
            // window (t), so an estoc starts on the caster and reaches full length by the end of ActiveDuration.
            float3 capsuleB = math.lerp(capsuleA, offsetPoint, t);

            float3 position = isCapsule ? (capsuleA + capsuleB) * 0.5f : offsetPoint;
            float queryRadius = isCapsule
                ? math.distance(capsuleA, capsuleB) * 0.5f + area.RadiusStart
                : currentRadius;

            var filter = new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = area.TargetLayers };
            var hits = new NativeList<DistanceHit>(64, Allocator.Temp);
            CollisionWorld.OverlapSphere(position, queryRadius, ref hits, filter);
            var random = Random.CreateFromIndex((uint)(entity.Index + 1));

            EDamageShakeSource shakeSource;
            if ((area.Tags & ESpellTag.Explosive) != 0)
                shakeSource = EDamageShakeSource.Explosion;
            else if (BossLookup.TryGetComponent(area.Caster, out var casterBoss))
                shakeSource = casterBoss.Kind == EBossKind.FinalBoss ? EDamageShakeSource.Boss : EDamageShakeSource.Elite;
            else
                shakeSource = EDamageShakeSource.Enemy;

            int dbIndex = -1;
            Entity caster = Entity.Null;
            if (SpellSourceLookup.TryGetComponent(entity, out var spellSource))
            {
                dbIndex = spellSource.DatabaseIndex;
                caster = spellSource.CasterEntity;
            }

            for (int i = 0; i < hits.Length; i++)
            {
                Entity hitEntity = hits[i].Entity;
                if (hitEntity == entity || hitEntity == Entity.Null)
                    continue;
                if (!DamageBufferLookup.HasBuffer(hitEntity))
                    continue;
                if (DestructibleLookup.HasComponent(hitEntity) && !DestructibleLookup.IsComponentEnabled(hitEntity))
                    continue;
                if (IsInHitMemory(hitMemory, hitEntity))
                    continue;

                bool inShape = isCapsule
                    ? CapsuleContains(capsuleA, capsuleB, area.RadiusStart, hits[i].Position)
                    : IsInShape(area.Shape, position, rotation, currentRadius, area.HalfAngle, currentSweep,
                        area.RingThickness, hits[i].Position);
                if (!inShape)
                    continue;

                // Crit is rolled AND its multiplier applied inside ResolveHit — the Burst path used to
                // write a cosmetic IsCritical while dealing base damage (half of the "crit gruyère").
                var action = HitAction.MakeDamage(area.Damage, area.CritChance, area.CritMultiplier, area.Tags);
                var source = new HitSource
                {
                    Caster = caster,
                    DatabaseIndex = dbIndex,
                    PushOrigin = PlayerPosition,
                    Shake = shakeSource,
                };
                ResolveHit.Apply(in Resolve, ECB, chunkIndex, hitEntity, in action, in source, ref random);

                hitMemory.Add(new HitEntityMemory { HitEntity = hitEntity, LastHitTime = 0f });
            }

            hits.Dispose();
        }
    }

    // ── OverTime cadence (was ProcessTickDamageJob) ──
    [BurstCompile]
    private partial struct OverTimeZoneJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public CollisionWorld CollisionWorld;

        [ReadOnly] public ComponentLookup<LiveStats> LiveStatsLookup;
        [ReadOnly] public ComponentLookup<LocalToWorld> LtwLookup;
        [ReadOnly] public ComponentLookup<DestroyEntityFlag> DestroyFlagLookup;
        [ReadOnly] public BufferLookup<DamageBufferElement> DamageBufferLookup;

        public ResolveHitContext Resolve;
        public uint Seed;

        private void Execute([ChunkIndexInQuery] int chunkIndex, Entity zoneEntity,
            ref AreaAttack area, in LocalToWorld zoneTransform,
            ref DynamicBuffer<TickDamageTarget> targets, in SpellSource spellSource)
        {
            if (area.Cadence != EZoneCadence.OverTime)
                return;
            if (!LiveStatsLookup.HasComponent(area.Caster))
                return;

            float3 zonePos = math.transform(zoneTransform.Value, area.Offset); // local Offset → world (matches visual)
            quaternion zoneRot = zoneTransform.Rotation;
            float areaRadius = area.RadiusStart; // OverTime is static (RadiusStart == RadiusEnd)

            float queryRadius = areaRadius;
            if (area.Shape == EAttackAreaShape.Ring)
                queryRadius = areaRadius + area.RingThickness * 0.5f;

            // Exit detection — drop targets outside the shape or destroyed.
            for (int i = targets.Length - 1; i >= 0; i--)
            {
                Entity target = targets[i].Value;

                if (DestroyFlagLookup.HasComponent(target) && DestroyFlagLookup.IsComponentEnabled(target))
                {
                    targets.RemoveAt(i);
                    continue;
                }

                bool outOfRange = true;
                if (LtwLookup.HasComponent(target))
                {
                    float3 targetPos = LtwLookup[target].Position;
                    if (area.Shape == EAttackAreaShape.Circle)
                        outOfRange = math.distance(zonePos, targetPos) > areaRadius;
                    else
                        outOfRange = !IsInShape(area.Shape, zonePos, zoneRot, areaRadius, area.HalfAngle, 0f,
                            area.RingThickness, targetPos);
                }

                if (outOfRange)
                    targets.RemoveAt(i);
            }

            area.ElapsedTime += DeltaTime;
            if (area.ElapsedTime < area.TickRate)
                return;
            area.ElapsedTime = 0f;

            var filter = new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = area.TargetLayers };
            var hits = new NativeList<DistanceHit>(16, Allocator.Temp);
            CollisionWorld.OverlapSphere(zonePos, queryRadius, ref hits, filter);

            for (int j = 0; j < hits.Length; j++)
            {
                Entity hitEntity = hits[j].Entity;
                if (hitEntity == zoneEntity)
                    continue;
                if (!DamageBufferLookup.HasBuffer(hitEntity))
                    continue;
                if (DestroyFlagLookup.HasComponent(hitEntity) && DestroyFlagLookup.IsComponentEnabled(hitEntity))
                    continue;

                if (area.Shape != EAttackAreaShape.Circle)
                {
                    float3 hitPos = LtwLookup[hitEntity].Position;
                    if (!IsInShape(area.Shape, zonePos, zoneRot, areaRadius, area.HalfAngle, 0f, area.RingThickness, hitPos))
                        continue;
                }

                bool alreadyTracked = false;
                for (int k = 0; k < targets.Length; k++)
                {
                    if (targets[k].Value == hitEntity)
                    {
                        alreadyTracked = true;
                        break;
                    }
                }

                if (!alreadyTracked)
                    targets.Add(new TickDamageTarget { Value = hitEntity });
            }

            hits.Dispose();

            for (int i = 0; i < targets.Length; i++)
            {
                Entity target = targets[i].Value;

                // OverTime ticks now crit too — the tick used to deal flat damage with no roll at all
                // (the other half of the "crit gruyère").
                var action = HitAction.MakeDamage(area.Damage, area.CritChance, area.CritMultiplier, area.Tags);
                var source = new HitSource
                {
                    Caster = spellSource.CasterEntity,
                    DatabaseIndex = spellSource.DatabaseIndex,
                    PushOrigin = zonePos,
                    Shake = EDamageShakeSource.DoT,
                };
                var rng = Random.CreateFromIndex(Seed ^ (uint)(target.Index + 1));
                ResolveHit.Apply(in Resolve, ECB, chunkIndex, target, in action, in source, ref rng);
            }
        }
    }

    // ── Damage tracking (shared) ──
    [BurstCompile]
    private struct TrackDamageJob : IJob
    {
        public NativeQueue<SpellDamageEvent> DamageEventsQueue;
        public BufferLookup<ActiveSpell> ActiveSpellLookup;
        public Entity PlayerEntity;

        public void Execute()
        {
            var sums = new NativeHashMap<int, int>(16, Allocator.Temp);

            while (DamageEventsQueue.TryDequeue(out var evt))
            {
                if (sums.ContainsKey(evt.DatabaseIndex))
                    sums[evt.DatabaseIndex] += evt.DamageAmount;
                else
                    sums.Add(evt.DatabaseIndex, evt.DamageAmount);
            }

            if (ActiveSpellLookup.TryGetBuffer(PlayerEntity, out var buffer))
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    var spell = buffer[i];
                    if (sums.TryGetValue(spell.DatabaseIndex, out int totalAdded))
                    {
                        spell.TotalDamageDealt += totalAdded;
                        buffer[i] = spell;
                    }
                }
            }

            sums.Dispose();
        }
    }
}
