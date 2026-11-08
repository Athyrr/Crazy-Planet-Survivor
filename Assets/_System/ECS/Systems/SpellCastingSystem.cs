using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PlayerSpawnerSystem))]
[UpdateAfter(typeof(SpellStatsCalculationSystem))]
[BurstCompile]
public partial struct SpellCastingSystem : ISystem
{
    private ComponentLookup<Player> _playerLookup;
    private ComponentLookup<Enemy> _enemyLookup;
    private ComponentLookup<Team> _teamLookup;
    private ComponentLookup<LocalToWorld> _localToWorldLookup;

    private BufferLookup<ActiveSpell> _activeSpellLookup;
    private EntityStorageInfoLookup _entityInfo;

    // Per-instance spawn lookups (transform, movement, combat, lifetime, capabilities, source)
    private SpellSpawnLookups _spawn;

    // Behaviors
    private ComponentLookup<PhysicsCollider> _colliderLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<SpellPrefab>();
        state.RequireForUpdate<ActiveSpell>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<CastSpellRequest>();
        state.RequireForUpdate<PhysicsWorldSingleton>();

        _playerLookup = SystemAPI.GetComponentLookup<Player>(true);
        _enemyLookup = SystemAPI.GetComponentLookup<Enemy>(true);
        _teamLookup = SystemAPI.GetComponentLookup<Team>(true);
        _localToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true);
        _activeSpellLookup = SystemAPI.GetBufferLookup<ActiveSpell>(true);
        _entityInfo = state.GetEntityStorageInfoLookup();
        _spawn = SpellSpawnLookups.Create(ref state);
        _colliderLookup = SystemAPI.GetComponentLookup<PhysicsCollider>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        // Update lookups
        _playerLookup.Update(ref state);
        _enemyLookup.Update(ref state);
        _teamLookup.Update(ref state);
        _localToWorldLookup.Update(ref state);
        _activeSpellLookup.Update(ref state);
        _entityInfo.Update(ref state);

        _spawn.Update(ref state);
        _colliderLookup.Update(ref state);

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        var spellDatabase = SystemAPI.GetSingleton<SpellsDatabase>();
        var mainSpellPrefabs = SystemAPI.GetSingletonBuffer<SpellPrefab>(true);

        Entity playerEntity = SystemAPI.TryGetSingletonEntity<Player>(out var playerSingleton)
            ? playerSingleton
            : Entity.Null;

        // Enemy-cast spells scale their damage with run difficulty (same clock as enemy HP/contact damage).
        EnemyScalingConfig scaleCfg = SystemAPI.TryGetSingleton<EnemyScalingConfig>(out var scfg)
            ? scfg
            : EnemyScalingConfig.Default;
        float enemyDamageMult = 1f;
        if (SystemAPI.TryGetSingleton<RunProgression>(out var runProg))
            enemyDamageMult = scaleCfg.ComputeDamageMult(runProg.Timer, runProg.EnemiesKilledCount);

        // Active planet's world-space center. The surface normal at the caster is derived from this,
        // so an off-origin planet no longer tilts every Area spell. Fallback to zero for edge cases
        // where the cast system runs without a planet (should not happen in gameplay).
        float3 planetCenter = SystemAPI.TryGetSingleton<PlanetData>(out var planetData)
            ? planetData.Center
            : float3.zero;

        var castJob = new CastSpellJob
        {
            ECB = ecb.AsParallelWriter(),
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1,
            PlayerEntity = playerEntity,
            EnemyDamageMult = enemyDamageMult,
            PlanetCenter = planetCenter,
            CollisionWorld = physicsWorldSingleton.CollisionWorld,
            SpellDatabaseRef = spellDatabase.Blobs,
            MainSpellPrefabs = mainSpellPrefabs,

            PlayerLookup = _playerLookup,
            EnemyLookup = _enemyLookup,
            TeamLookup = _teamLookup,
            LocalToWorldLookup = _localToWorldLookup,
            ActiveSpellLookup = _activeSpellLookup,
            EntityInfo = _entityInfo,

            Spawn = _spawn,
            ColliderLookup = _colliderLookup,

            QueueEntity = SystemAPI.TryGetSingletonEntity<EmitRequestQueue>(out var queue) ? queue : Entity.Null,
            MaxSplitDepth = SystemAPI.TryGetSingleton<EmitterConfig>(out var emitterCfg)
                ? emitterCfg.MaxSplitDepth
                : EmitterConfig.Default.MaxSplitDepth,
        };

        state.Dependency = castJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct CastSpellJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public uint Seed;
        public Entity PlayerEntity;
        public float EnemyDamageMult;
        public float3 PlanetCenter;

        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public DynamicBuffer<SpellPrefab> MainSpellPrefabs;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;
        [ReadOnly] public ComponentLookup<Team> TeamLookup;
        [ReadOnly] public ComponentLookup<LocalToWorld> LocalToWorldLookup;
        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;
        [ReadOnly] public EntityStorageInfoLookup EntityInfo;

        public SpellSpawnLookups Spawn; // all [ReadOnly] inside

        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;

        // Emitters: EmitRequest queue singleton (Entity.Null = no emission system) and Split depth cap.
        public Entity QueueEntity;
        public int MaxSplitDepth;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity requestEntity, in CastSpellRequest request)
        {
            if (!SpellDatabaseRef.IsCreated || !Spawn.Transform.HasComponent(request.Caster))
            {
                ECB.DestroyEntity(chunkIndex, requestEntity);
                return;
            }

            ref var baseSpellData = ref SpellDatabaseRef.Value.Spells[request.DatabaseIndex];
            var spellPrefab = MainSpellPrefabs[request.DatabaseIndex].Prefab;

            // No prefab = nothing to spawn: bail out before any read of the prefab.
            if (spellPrefab == Entity.Null)
            {
                ECB.DestroyEntity(chunkIndex, requestEntity);
                return;
            }

            ActiveSpell activeSpell = default;
            bool found = false;

            // Try to find the ActiveSpell instance to get final calculated values
            if (ActiveSpellLookup.TryGetBuffer(request.Caster, out var activeSpells))
            {
                for (int i = 0; i < activeSpells.Length; i++)
                {
                    if (activeSpells[i].DatabaseIndex == request.DatabaseIndex)
                    {
                        activeSpell = activeSpells[i];
                        found = true;
                        break;
                    }
                }
            }

            // If active spell not found -> return
            if (!found)
            {
                ECB.DestroyEntity(chunkIndex, requestEntity);
                return;
            }

            // Lifetime = -1 attached owner (FireOrbs): skip the cast while the persistent instance lives;
            // an invalid (destroyed) instance allows the recast.
            if (baseSpellData.Lifetime < 0f && activeSpell.PersistentInstance != Entity.Null
                && EntityInfo.Exists(activeSpell.PersistentInstance))
            {
                ECB.DestroyEntity(chunkIndex, requestEntity);
                return;
            }

            // Extract values directly
            float finalDamage = activeSpell.FinalDamage;
            float finalSpeed = activeSpell.FinalSpeed;
            float finalRange = activeSpell.FinalRange;
            float finalDuration = activeSpell.FinalDuration;
            int finalAmount = activeSpell.FinalAmount;
            float finalSize = activeSpell.FinalSize;
            float finalCritChance = activeSpell.FinalCritChance;
            float finalCritDamageMultiplier = activeSpell.FinalCritDamageMultiplier;
            float finalTickRate = activeSpell.FinalTickRate;
            int finalBounces = activeSpell.FinalBounces;
            int finalPierce = activeSpell.FinalPierces;
            float finalBounceRange = activeSpell.FinalBounceRange;

            // FinalTags/FinalEffects are already composed by SpellStatsCalculationSystem, which runs
            // before this system ([UpdateAfter(typeof(SpellStatsCalculationSystem))], line 11) — read the cache
            // instead of recomputing tags here (recomputing would silently drop the derived status bits).
            ESpellTag totalTags = activeSpell.FinalTags;

            var casterTransform = Spawn.Transform[request.Caster];
            var random = Random.CreateFromIndex(Seed);


            // Multi-cast — how Amount is consumed at cast time (Single/Spread/MultiTarget).
            // Enemy-side casts with MultiTarget degenerate to Spread (handled once the caster team is known, below).
            var multiCast = baseSpellData.MultiCast;
            int totalCount = multiCast == ESpellMultiCast.Single ? 1 : math.max(1, finalAmount);

            // Targeting
            float3 targetPosition = casterTransform.Position;
            bool targetFound = false;
            Entity targetEntity = Entity.Null;
            // Allegiance (spec allegeance-summon §1): "player side" = the Player or a Summon of team Player.
            ETeam casterTeam = TeamUtils.Resolve(request.Caster, in TeamLookup, in PlayerLookup);
            bool playerSide = casterTeam == ETeam.Player;

            // Enemy-side MultiTarget degenerates to Spread — only one player target exists.
            if (multiCast == ESpellMultiCast.MultiTarget && !playerSide)
                multiCast = ESpellMultiCast.Spread;

            // Enemy-side casts scale their damage with run difficulty — never a Summon of the player.
            if (casterTeam == ETeam.Enemy)
                finalDamage *= EnemyDamageMult;

            // BaseSpawnOffset is a caster-local offset (x = right, y = up, z = forward).
            // Rotate it by the caster rotation so it points relative to where the caster faces.
            float3 baseSpawnPos =
                casterTransform.Position + math.mul(casterTransform.Rotation, baseSpellData.BaseSpawnOffset);
            quaternion baseRotation = casterTransform.Rotation;
            float3 fireDirection = casterTransform.Forward();

            float3 planetCenter = PlanetCenter;

            // todo mb store only CollidesWith
            var filter = new CollisionFilter
            {
                // BelongsTo = CollisionLayers.Spell
                CollidesWith = TeamUtils.TargetLayers(casterTeam),
            };

            // A caster-anchored melee area (CopyEntityPosition) targets only within its real hitbox reach
            // (offset + radius), so the cast is coherent with what the collision can hit. Ranged strikes
            // and projectiles keep their chosen cast range (FinalRange).
            float targetingRange = finalRange;
            if (Spawn.AreaAttack.HasComponent(spellPrefab) && Spawn.CopyPosition.HasComponent(spellPrefab))
            {
                var meleeArea = Spawn.AreaAttack[spellPrefab];
                float meleeMaxRadius = math.max(meleeArea.RadiusStart, meleeArea.RadiusEnd);
                targetingRange = math.max(1f, finalSize * (math.length(meleeArea.Offset) + meleeMaxRadius));
            }

            switch (baseSpellData.TargetingMode)
            {
                case ESpellTargetingMode.OnCaster:
                    targetPosition = casterTransform.Position;
                    targetEntity = request.Caster;
                    targetFound = true;
                    break;

                case ESpellTargetingMode.CastForward:
                    targetPosition = casterTransform.Position +
                                     (casterTransform.Forward() * finalRange);
                    targetFound = true;
                    break;

                case ESpellTargetingMode.NearestTarget:
                    if (casterTeam == ETeam.Enemy)
                    {
                        // Enemy spells target the player directly. A physics query on the Player
                        // layer also matches the planet collider (mis-categorized onto that layer,
                        // sitting at the world origin), so resolve the player explicitly instead.
                        if (PlayerEntity == Entity.Null)
                        {
                            ECB.DestroyEntity(chunkIndex, requestEntity);
                            return;
                        }

                        targetEntity = PlayerEntity;
                        targetPosition = LocalToWorldLookup.HasComponent(PlayerEntity)
                            ? LocalToWorldLookup[PlayerEntity].Position
                            : casterTransform.Position;
                        targetFound = true;
                    }
                    else
                    {
                        PointDistanceInput input = new PointDistanceInput
                        {
                            Position = casterTransform.Position,
                            MaxDistance = targetingRange,
                            Filter = new CollisionFilter
                            {
                                BelongsTo = CollisionLayers.Raycast,
                                CollidesWith = TeamUtils.HostileLayers(casterTeam),
                            }
                        };
                        if (CollisionWorld.CalculateDistance(input, out DistanceHit hit))
                        {
                            targetEntity = hit.Entity;
                            targetPosition = LocalToWorldLookup.HasComponent(hit.Entity)
                                ? LocalToWorldLookup[hit.Entity].Position
                                : hit.Position;
                            targetFound = true;
                        }
                        else
                        {
                            // Fallback
                            ECB.DestroyEntity(chunkIndex, requestEntity);
                            return;
                        }
                    }

                    break;

                case ESpellTargetingMode.RandomInRange:
                    var groundFilter = new CollisionFilter
                    {
                        BelongsTo = CollisionLayers.Raycast,
                        CollidesWith = CollisionLayers.Landscape
                    };

                    if (PlanetUtils.GetRandomPointOnSurface(
                            ref CollisionWorld,
                            ref random,
                            casterTransform.Position,
                            planetCenter,
                            finalRange,
                            ref groundFilter,
                            out var p,
                            out var n))
                    {
                        targetPosition = p;
                        baseRotation = quaternion.LookRotationSafe(casterTransform.Forward(), n);
                        targetFound = true;
                    }

                    break;
            }

            // SPAWN CALCULATION (Position & Rotation Basis)
            float3 surfaceNormal = math.normalize(casterTransform.Position - planetCenter);

            bool isProjectile = Spawn.LinearMovement.HasComponent(spellPrefab);

            // Caster-anchored (melee/aura that follows the player, via CopyEntityPosition) vs
            // world-anchored (a ranged strike that lands ON the target, e.g. lightning).
            bool casterAnchored = Spawn.CopyPosition.HasComponent(spellPrefab);
            bool isArea = Spawn.AreaAttack.HasComponent(spellPrefab);

            if (targetFound && (isProjectile || isArea))
            {
                // Facing = caster→target (the aim), independent of the spawn offset, so a spell can sit
                // in front of the caster and still face the target (close/melee targets no longer
                // collapse the direction to ~0 and leave it stuck on the caster's raw rotation).
                float3 toTarget = targetPosition - casterTransform.Position;
                if (math.lengthsq(toTarget) > math.EPSILON)
                {
                    fireDirection = math.normalize(toTarget);
                    baseRotation = quaternion.LookRotationSafe(fireDirection, surfaceNormal);
                }

                // A ranged area strike (no caster anchor) lands AT the target point; caster-anchored
                // melee areas and projectiles keep spawning near the caster.
                if (isArea && !casterAnchored)
                    baseSpawnPos = targetPosition;
            }
            else if (!isProjectile)
            {
                baseSpawnPos = targetPosition; // Area spells spawn on target
                baseRotation = quaternion.LookRotationSafe(casterTransform.Forward(), surfaceNormal);
            }

            // Rotation axis for the fan — the entity's local Y in world = surface normal at cast time
            // (baked in by LookRotationSafe(dir, surfaceNormal)). Fixes the previous world-Y bug that
            // tilted the fan on a curved planet.
            float3 upAxis = math.mul(baseRotation, math.up());

            // MultiTarget: acquire up to N distinct nearest enemies within FinalRange.
            // Zero found → cancel this cast cleanly. Surplus (Amount > found) is fanned from the caster.
            NativeList<float3> multiTargets = default;
            int multiTargetsFound = 0;
            if (multiCast == ESpellMultiCast.MultiTarget && playerSide)
            {
                multiTargets = new NativeList<float3>(totalCount, Allocator.Temp);
                var mtFilter = new CollisionFilter
                {
                    BelongsTo = CollisionLayers.Raycast,
                    CollidesWith = TeamUtils.HostileLayers(casterTeam)
                };
                var mtHits = new NativeList<DistanceHit>(32, Allocator.Temp);
                CollisionWorld.OverlapSphere(casterTransform.Position, finalRange, ref mtHits, mtFilter);

                // Sort by distance so we pick the N nearest, then dedupe by entity (a target may span multiple colliders).
                mtHits.Sort(new DistanceHitDistanceComparer());
                var seen = new NativeHashSet<Entity>(totalCount, Allocator.Temp);
                for (int h = 0; h < mtHits.Length && multiTargets.Length < totalCount; h++)
                {
                    if (seen.Add(mtHits[h].Entity))
                    {
                        multiTargets.Add(LocalToWorldLookup.HasComponent(mtHits[h].Entity)
                            ? LocalToWorldLookup[mtHits[h].Entity].Position
                            : mtHits[h].Position);
                    }
                }
                seen.Dispose();
                mtHits.Dispose();

                multiTargetsFound = multiTargets.Length;

                if (multiTargetsFound == 0)
                {
                    multiTargets.Dispose();
                    ECB.DestroyEntity(chunkIndex, requestEntity);
                    return;
                }
            }

            // Emitters: only player-side casts post. Root instances carry the lineage (LineageId = first instance).
            bool postEmitters = playerSide && QueueEntity != Entity.Null && baseSpellData.Emitters.Length > 0;
            bool hasOnActivation = postEmitters && EmitterQueries.HasTrigger(ref baseSpellData, EEmitterTrigger.OnActivation);
            byte rootDepth = (byte)math.clamp(math.min(activeSpell.FinalSplitDepth, MaxSplitDepth), 0, 255);
            Entity lineageId = Entity.Null;

            // Spawn loop
            for (int i = 0; i < totalCount; i++)
            {
                // Per-instance spawn (position, rotation, direction) based on MultiCast.
                float3 spawnPos = baseSpawnPos;
                quaternion finalRotation = baseRotation;
                float3 finalDirection = fireDirection;

                if (multiCast == ESpellMultiCast.MultiTarget && i < multiTargetsFound)
                {
                    // Aim at this specific target. For a world-anchored Area, spawn AT the target point
                    // (like a strike); for other families, keep caster-based spawn and fire toward target.
                    float3 targetPos = multiTargets[i];
                    float3 toTarget = targetPos - casterTransform.Position;
                    if (math.lengthsq(toTarget) > math.EPSILON)
                    {
                        finalDirection = math.normalize(toTarget);
                        finalRotation = quaternion.LookRotationSafe(finalDirection, upAxis);
                    }
                    if (isArea && !casterAnchored)
                    {
                        spawnPos = targetPos;
                    }
                }
                else if (multiCast == ESpellMultiCast.Spread
                         || (multiCast == ESpellMultiCast.MultiTarget && i >= multiTargetsFound))
                {
                    // Fan pattern — full Spread mode, or MultiTarget surplus fanned from the caster.
                    int spreadTotal = multiCast == ESpellMultiCast.MultiTarget ? (totalCount - multiTargetsFound) : totalCount;
                    int spreadIdx   = multiCast == ESpellMultiCast.MultiTarget ? (i - multiTargetsFound) : i;

                    if (spreadTotal > 1)
                    {
                        // Total-spread cap: tighten per-instance angle when N × SpreadAngle > MaxSpread.
                        float perAngle = baseSpellData.SpreadAngleDegrees;
                        float totalSpread = (spreadTotal - 1) * perAngle;
                        if (totalSpread > baseSpellData.MaxSpreadDegrees)
                            perAngle = baseSpellData.MaxSpreadDegrees / (spreadTotal - 1);
                        float startAngle = -(spreadTotal - 1) * perAngle * 0.5f;

                        float angle = startAngle + spreadIdx * perAngle;
                        finalRotation = math.mul(quaternion.AxisAngle(upAxis, math.radians(angle)), baseRotation);
                        finalDirection = math.forward(finalRotation);
                    }
                }
                // Single mode: no override, use base values.

                // Instance placed: the shared helper writes source, transform, movement, combat, zone,
                // lifetime, self-rotate and Bounce/Pierce (same order as the former inline steps).
                var spellEntity = SpellSpawnUtils.SpawnSpellInstance(in Spawn, ECB, chunkIndex, new SpellSpawnParams
                {
                    Prefab = spellPrefab,
                    Caster = request.Caster,
                    DatabaseIndex = request.DatabaseIndex,
                    Position = spawnPos,
                    Rotation = finalRotation,
                    Direction = finalDirection,
                    Target = targetEntity,
                    CasterOffset = baseSpellData.BaseSpawnOffset,
                    Scale = finalSize,
                    Damage = finalDamage,
                    Speed = finalSpeed,
                    Duration = finalDuration,
                    TickRate = finalTickRate,
                    CritChance = finalCritChance,
                    CritMultiplier = finalCritDamageMultiplier,
                    Tags = totalTags,
                    Effects = activeSpell.FinalEffects,
                    TargetLayers = filter.CollidesWith,
                    Allowed = baseSpellData.AllowedCapabilities,
                    Bounces = finalBounces,
                    BounceRange = finalBounceRange,
                    Pierces = finalPierce,
                });

                if (baseSpellData.ReHitDelayOverride > 0f)
                    ECB.AddComponent(chunkIndex, spellEntity, new ReHitDelay { Value = baseSpellData.ReHitDelayOverride });

                if (postEmitters)
                {
                    if (i == 0)
                        lineageId = spellEntity; // deferred entity: remapped at playback (same ECB)
                    ECB.AddComponent(chunkIndex, spellEntity, new SpellLineage
                    {
                        LineageId = lineageId, Generation = 0, DepthLeft = rootDepth, ShapeDatabaseIndex = request.DatabaseIndex,
                    });
                    if (hasOnActivation)
                    {
                        ECB.AppendToBuffer(chunkIndex, QueueEntity, new EmitRequest
                        {
                            RootDatabaseIndex = request.DatabaseIndex,
                            ShapeDatabaseIndex = request.DatabaseIndex,
                            Caster = request.Caster,
                            Trigger = EEmitterTrigger.OnActivation,
                            Position = spawnPos,
                            Direction = finalDirection,
                            Rotation = finalRotation,
                            Owner = spellEntity,
                            LineageId = lineageId,
                            Generation = 0,
                            DepthLeft = rootDepth,
                            TriggerCrit = false,
                            IgnoreTarget = Entity.Null,
                            RollSeed = random.NextUInt(),
                        });
                    }
                }
            }

            if (multiTargets.IsCreated)
                multiTargets.Dispose();
            ECB.DestroyEntity(chunkIndex, requestEntity);
        }

        /// <summary>Sort <see cref="DistanceHit"/> results by ascending distance so we can pick the N nearest.</summary>
        private struct DistanceHitDistanceComparer : IComparer<DistanceHit>
        {
            public int Compare(DistanceHit a, DistanceHit b) => a.Distance.CompareTo(b.Distance);
        }
    }
}