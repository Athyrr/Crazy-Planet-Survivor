using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// System that handles tick damage (aura/zone spells).
/// Uses OverlapSphere for entry detection (on tick) + distance check for exit (every frame).
/// Tracked via TickDamageTarget buffer per zone entity. Damage applied via ECB for thread safety.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct TickDamageSystem : ISystem
{
    private ComponentLookup<FinalStats> _finalStatsLookup;
    private ComponentLookup<LocalToWorld> _transformLookup;
    private ComponentLookup<DestroyEntityFlag> _destroyFlagLookup;
    private BufferLookup<DamageBufferElement> _damageBufferLookup;
    private BufferLookup<ActiveSpell> _activeSpellBufferLookup;

    private EntityQuery _playerQuery;
    private NativeQueue<SpellDamageEvent> _damageEventsQueue;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<Player>();

        _playerQuery = state.GetEntityQuery(ComponentType.ReadOnly<Player>());

        _finalStatsLookup = state.GetComponentLookup<FinalStats>(true);
        _transformLookup = state.GetComponentLookup<LocalToWorld>(true);
        _destroyFlagLookup = state.GetComponentLookup<DestroyEntityFlag>(true);
        _damageBufferLookup = state.GetBufferLookup<DamageBufferElement>(true);
        _activeSpellBufferLookup = state.GetBufferLookup<ActiveSpell>(false);

        _damageEventsQueue = new NativeQueue<SpellDamageEvent>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_damageEventsQueue.IsCreated)
            _damageEventsQueue.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (_playerQuery.IsEmpty)
            return;

        if (gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        _finalStatsLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _destroyFlagLookup.Update(ref state);
        _damageBufferLookup.Update(ref state);
        _activeSpellBufferLookup.Update(ref state);

        // Tick damage processing — entry detection via OverlapSphere,
        // exit detection via distance check, damage via ECB
        var processTickJob = new ProcessTickDamageJob
        {
            ECB = ecb.AsParallelWriter(),
            DeltaTime = SystemAPI.Time.DeltaTime,
            CollisionWorld = collisionWorld,

            FinalStatsLookup = _finalStatsLookup,
            TransformLookup = _transformLookup,
            DestroyFlagLookup = _destroyFlagLookup,
            DamageBufferLookup = _damageBufferLookup,

            DamageEventsWriter = _damageEventsQueue.AsParallelWriter(),
        };
        JobHandle processHandle = processTickJob.ScheduleParallel(state.Dependency);

        // Track total damage dealt into ActiveSpell buffer
        var trackJob = new TrackTickDamageJob
        {
            DamageEventsQueue = _damageEventsQueue,
            ActiveSpellLookup = _activeSpellBufferLookup,
            PlayerEntity = SystemAPI.GetSingletonEntity<Player>(),
        };
        state.Dependency = trackJob.Schedule(processHandle);
    }

    /// <summary>
    /// Iterates zone entities (DamageOnTick + TickDamageTarget buffer).
    /// Entry: OverlapSphere on tick to find new targets.
    /// Exit: distance check every frame.
    /// Damage: applied via ECB (thread-safe).
    /// </summary>
    [BurstCompile]
    private partial struct ProcessTickDamageJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public CollisionWorld CollisionWorld;

        [ReadOnly] public ComponentLookup<FinalStats> FinalStatsLookup;
        [ReadOnly] public ComponentLookup<LocalToWorld> TransformLookup;
        [ReadOnly] public ComponentLookup<DestroyEntityFlag> DestroyFlagLookup;
        [ReadOnly] public BufferLookup<DamageBufferElement> DamageBufferLookup;

        public NativeQueue<SpellDamageEvent>.ParallelWriter DamageEventsWriter;

        private void Execute([ChunkIndexInQuery] int chunkIndex, Entity zoneEntity,
            ref DamageOnTick damageOnTick, in LocalToWorld zoneTransform,
            ref DynamicBuffer<TickDamageTarget> targets, in SpellSource spellSource)
        {
            if (!FinalStatsLookup.HasComponent(damageOnTick.Caster))
                return;

            float3 zonePos = zoneTransform.Position;
            quaternion zoneRot = zoneTransform.Rotation;
            float areaRadius = damageOnTick.AreaRadius;

            // For Ring shapes, expand the query radius to cover the full ring band
            float queryRadius = areaRadius;
            if (damageOnTick.Shape == EAttackAreaShape.Ring)
                queryRadius = areaRadius + damageOnTick.RingThickness * 0.5f;

            // Exit detection — remove targets outside shape or destroyed
            for (int i = targets.Length - 1; i >= 0; i--)
            {
                Entity target = targets[i].Value;

                if (DestroyFlagLookup.HasComponent(target) && DestroyFlagLookup.IsComponentEnabled(target))
                {
                    targets.RemoveAt(i);
                    continue;
                }

                bool outOfRange = true;
                if (TransformLookup.HasComponent(target))
                {
                    float3 targetPos = TransformLookup[target].Position;

                    if (damageOnTick.Shape == EAttackAreaShape.Circle)
                    {
                        outOfRange = math.distance(zonePos, targetPos) > areaRadius;
                    }
                    else
                    {
                        // Cone/Ring: use shape check for exit
                        outOfRange = !IsInShape(zonePos, zoneRot, damageOnTick, targetPos);
                    }
                }

                if (outOfRange)
                    targets.RemoveAt(i);
            }

            // Tick timer
            damageOnTick.ElapsedTime += DeltaTime;
            if (damageOnTick.ElapsedTime < damageOnTick.TickRate)
                return;

            damageOnTick.ElapsedTime = 0f;

            // Find new targets via OverlapSphere
            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = damageOnTick.TargetLayers
            };

            var hits = new NativeList<DistanceHit>(16, Allocator.Temp);
            CollisionWorld.OverlapSphere(zonePos, queryRadius, ref hits, filter);

            for (int j = 0; j < hits.Length; j++)
            {
                Entity hitEntity = hits[j].Entity;
                if (hitEntity == zoneEntity)
                    continue;

                // Only track entities that can receive damage
                if (!DamageBufferLookup.HasBuffer(hitEntity))
                    continue;

                if (DestroyFlagLookup.HasComponent(hitEntity) && DestroyFlagLookup.IsComponentEnabled(hitEntity))
                    continue;

                // Shape filtering (for Cone/Ring, already within OverlapSphere radius)
                if (damageOnTick.Shape != EAttackAreaShape.Circle)
                {
                    float3 hitPos = TransformLookup[hitEntity].Position;
                    if (!IsInShape(zonePos, zoneRot, damageOnTick, hitPos))
                        continue;
                }

                // Deduplicate against existing tracked targets
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

            //  Apply tick damage to all tracked targets 
            float damage = damageOnTick.DamagePerTick;

            for (int i = 0; i < targets.Length; i++)
            {
                Entity target = targets[i].Value;

                ECB.AppendToBuffer(chunkIndex, target, new DamageBufferElement
                {
                    Damage = (int)damage,
                    Tag = damageOnTick.Tags,
                    ShakeSource = EDamageShakeSource.DoT,
                });

                DamageEventsWriter.Enqueue(new SpellDamageEvent
                {
                    DatabaseIndex = spellSource.DatabaseIndex,
                    DamageAmount = (int)damage,
                });
            }
        }

        /// <summary>Shape-based filtering for non-Circle tick zones.</summary>
        private static bool IsInShape(float3 position, quaternion rotation,
            in DamageOnTick damageOnTick, float3 hitPosition)
        {
            switch (damageOnTick.Shape)
            {
                case EAttackAreaShape.Circle:
                    return true; // Already handled by OverlapSphere radius

                case EAttackAreaShape.Cone:
                {
                    float3 forward = math.forward(rotation);
                    float3 toHit = math.normalize(hitPosition - position);
                    float cosAngle = math.dot(forward, toHit);
                    return cosAngle >= math.cos(damageOnTick.HalfAngle);
                }

                case EAttackAreaShape.Ring:
                {
                    float dist = math.distance(position, hitPosition);
                    float halfThickness = damageOnTick.RingThickness * 0.5f;
                    float distFromRing = math.abs(dist - damageOnTick.AreaRadius);
                    return distFromRing <= halfThickness;
                }

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Retrieves damage event queue and accumulates total damage dealt into ActiveSpell buffer.
    /// </summary>
    [BurstCompile]
    private struct TrackTickDamageJob : IJob
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
