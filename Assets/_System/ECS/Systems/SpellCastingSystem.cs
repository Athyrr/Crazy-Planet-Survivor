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
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<LocalToWorld> _localToWorldLookup;

    private BufferLookup<ActiveSpell> _activeSpellLookup;

    // Behaviors
    private ComponentLookup<Lifetime> _lifetimeLookup;
    private ComponentLookup<PhysicsCollider> _colliderLookup;
    private ComponentLookup<AttachToCaster> _attachLookup;
    private ComponentLookup<CopyEntityPosition> _copyPositionLookup;
    private ComponentLookup<SelfRotate> _selfRotateLookup;

    private ComponentLookup<DamageOnContact> _damageOnContactLookup;
    private ComponentLookup<AreaAttack> _areaAttackLookup;

    private ComponentLookup<LinearMovement> _linearMovementLookup;
    private ComponentLookup<OrbitMovement> _orbitMovementLookup;
    private ComponentLookup<FollowTargetMovement> _followMovementLookup;

    private ComponentLookup<SubSpellsSpawner> _subSpellsSpawnerLookup;
    private ComponentLookup<SubSpellsLayout_Circle> _subSpellsCircleLayoutLookup;

    // Enableable Components
    private ComponentLookup<Bounce> _bounceLookup;
    private ComponentLookup<Pierce> _pierceLookup;
    private ComponentLookup<ExplodeOnContact> _explodeOnContactLookup;

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
        _transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        _localToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true);
        _activeSpellLookup = SystemAPI.GetBufferLookup<ActiveSpell>(true);
        _lifetimeLookup = SystemAPI.GetComponentLookup<Lifetime>(true);
        _colliderLookup = SystemAPI.GetComponentLookup<PhysicsCollider>(true);
        _attachLookup = SystemAPI.GetComponentLookup<AttachToCaster>(true);
        _copyPositionLookup = SystemAPI.GetComponentLookup<CopyEntityPosition>(true);
        _selfRotateLookup = SystemAPI.GetComponentLookup<SelfRotate>(true);
        _damageOnContactLookup = SystemAPI.GetComponentLookup<DamageOnContact>(true);
        _areaAttackLookup = SystemAPI.GetComponentLookup<AreaAttack>(true);
        _linearMovementLookup = SystemAPI.GetComponentLookup<LinearMovement>(true);
        _orbitMovementLookup = SystemAPI.GetComponentLookup<OrbitMovement>(true);
        _followMovementLookup = SystemAPI.GetComponentLookup<FollowTargetMovement>(true);
        _subSpellsSpawnerLookup = SystemAPI.GetComponentLookup<SubSpellsSpawner>(true);
        _subSpellsCircleLayoutLookup = SystemAPI.GetComponentLookup<SubSpellsLayout_Circle>(true);
        _bounceLookup = SystemAPI.GetComponentLookup<Bounce>(true);
        _pierceLookup = SystemAPI.GetComponentLookup<Pierce>(true);
        _explodeOnContactLookup = SystemAPI.GetComponentLookup<ExplodeOnContact>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        // Update lookups
        _playerLookup.Update(ref state);
        _enemyLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _localToWorldLookup.Update(ref state);
        _activeSpellLookup.Update(ref state);

        _lifetimeLookup.Update(ref state);
        _colliderLookup.Update(ref state);
        _attachLookup.Update(ref state);
        _copyPositionLookup.Update(ref state);
        _selfRotateLookup.Update(ref state);
        _damageOnContactLookup.Update(ref state);
        _areaAttackLookup.Update(ref state);
        _linearMovementLookup.Update(ref state);
        _orbitMovementLookup.Update(ref state);
        _followMovementLookup.Update(ref state);
        _subSpellsSpawnerLookup.Update(ref state);
        _subSpellsCircleLayoutLookup.Update(ref state);
        _bounceLookup.Update(ref state);
        _pierceLookup.Update(ref state);
        _explodeOnContactLookup.Update(ref state);

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        var spellDatabase = SystemAPI.GetSingleton<SpellsDatabase>();
        var mainSpellPrefabs = SystemAPI.GetSingletonBuffer<SpellPrefab>(true);
        var childSpellPrefabs = SystemAPI.GetSingletonBuffer<ChildSpellPrefab>(true);

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
            ChildSpellPrefabs = childSpellPrefabs,

            PlayerLookup = _playerLookup,
            EnemyLookup = _enemyLookup,
            TransformLookup = _transformLookup,
            LocalToWorldLookup = _localToWorldLookup,
            ActiveSpellLookup = _activeSpellLookup,

            LifetimeLookup = _lifetimeLookup,
            ColliderLookup = _colliderLookup,
            AttachLookup = _attachLookup,
            CopyPositionLookup = _copyPositionLookup,
            SelfRotateLookup = _selfRotateLookup,
            DamageOnContactLookup = _damageOnContactLookup,
            AreaAttackLookup = _areaAttackLookup,
            LinearMovementLookup = _linearMovementLookup,
            OrbitMovementLookup = _orbitMovementLookup,
            FollowMovementLookup = _followMovementLookup,
            SubSpellsSpawnerLookup = _subSpellsSpawnerLookup,
            SubSpellsCircleLayoutLookup = _subSpellsCircleLayoutLookup,
            BounceLookup = _bounceLookup,
            PierceLookup = _pierceLookup,
            ExplodeOnContactLookup = _explodeOnContactLookup
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
        [ReadOnly] public DynamicBuffer<ChildSpellPrefab> ChildSpellPrefabs;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<LocalToWorld> LocalToWorldLookup;
        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;

        [ReadOnly] public ComponentLookup<Lifetime> LifetimeLookup;
        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;
        [ReadOnly] public ComponentLookup<AttachToCaster> AttachLookup;
        [ReadOnly] public ComponentLookup<CopyEntityPosition> CopyPositionLookup;
        [ReadOnly] public ComponentLookup<SelfRotate> SelfRotateLookup;
        [ReadOnly] public ComponentLookup<DamageOnContact> DamageOnContactLookup;
        [ReadOnly] public ComponentLookup<AreaAttack> AreaAttackLookup;
        [ReadOnly] public ComponentLookup<LinearMovement> LinearMovementLookup;
        [ReadOnly] public ComponentLookup<OrbitMovement> OrbitMovementLookup;
        [ReadOnly] public ComponentLookup<FollowTargetMovement> FollowMovementLookup;
        [ReadOnly] public ComponentLookup<SubSpellsSpawner> SubSpellsSpawnerLookup;
        [ReadOnly] public ComponentLookup<SubSpellsLayout_Circle> SubSpellsCircleLayoutLookup;
        [ReadOnly] public ComponentLookup<Bounce> BounceLookup;
        [ReadOnly] public ComponentLookup<Pierce> PierceLookup;
        [ReadOnly] public ComponentLookup<ExplodeOnContact> ExplodeOnContactLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity requestEntity, in CastSpellRequest request)
        {
            if (!SpellDatabaseRef.IsCreated || !TransformLookup.HasComponent(request.Caster))
            {
                ECB.DestroyEntity(chunkIndex, requestEntity);
                return;
            }

            ref readonly var baseSpellData = ref SpellDatabaseRef.Value.Spells[request.DatabaseIndex];
            var spellPrefab = MainSpellPrefabs[request.DatabaseIndex].Prefab;

            if (spellPrefab == Entity.Null && baseSpellData.ChildPrefabIndex == -1)
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

            ESpellTag totalTags = baseSpellData.Tag | activeSpell.AddedTags;

            var casterTransform = TransformLookup[request.Caster];
            var spellPrefabTransform = TransformLookup[spellPrefab];
            var random = Random.CreateFromIndex(Seed);


            // Multi-cast — how Amount is consumed at cast time (Single/Spread/MultiTarget).
            // Orbital spawners always cast exactly 1 root; Amount flows to the spawner's child count elsewhere.
            // Enemy casts with MultiTarget degenerate to Spread (handled once isPlayerCaster is known, below).
            var multiCast = baseSpellData.MultiCast;
            int totalCount = multiCast == ESpellMultiCast.Single ? 1 : math.max(1, finalAmount);
            if (SubSpellsSpawnerLookup.HasComponent(spellPrefab))
                totalCount = 1;

            // Targeting
            float3 targetPosition = casterTransform.Position;
            bool targetFound = false;
            Entity targetEntity = Entity.Null;
            bool isPlayerCaster = PlayerLookup.HasComponent(request.Caster);

            // Enemy MultiTarget degenerates to Spread — only one player target exists.
            if (multiCast == ESpellMultiCast.MultiTarget && !isPlayerCaster)
                multiCast = ESpellMultiCast.Spread;

            // Enemy-cast spells scale their damage with run difficulty (player spells are untouched).
            if (!isPlayerCaster)
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
                CollidesWith = (isPlayerCaster ? CollisionLayers.Enemy : CollisionLayers.Player) |
                               CollisionLayers.Obstacle,
            };

            // A caster-anchored melee area (CopyEntityPosition) targets only within its real hitbox reach
            // (offset + radius), so the cast is coherent with what the collision can hit. Ranged strikes
            // and projectiles keep their chosen cast range (FinalRange).
            float targetingRange = finalRange;
            if (AreaAttackLookup.HasComponent(spellPrefab) && CopyPositionLookup.HasComponent(spellPrefab))
            {
                var meleeArea = AreaAttackLookup[spellPrefab];
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
                    if (!isPlayerCaster)
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
                                CollidesWith = CollisionLayers.Enemy,
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

            bool isProjectile = LinearMovementLookup.HasComponent(spellPrefab);
            bool isAttached = AttachLookup.HasComponent(spellPrefab);

            if (!isAttached)
            {
                // Caster-anchored (melee/aura that follows the player, via CopyEntityPosition) vs
                // world-anchored (a ranged strike that lands ON the target, e.g. lightning).
                bool casterAnchored = CopyPositionLookup.HasComponent(spellPrefab);
                bool isArea = AreaAttackLookup.HasComponent(spellPrefab);

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
            }

            // Rotation axis for the fan — the entity's local Y in world = surface normal at cast time
            // (baked in by LookRotationSafe(dir, surfaceNormal)). Fixes the previous world-Y bug that
            // tilted the fan on a curved planet.
            float3 upAxis = math.mul(baseRotation, math.up());

            // MultiTarget: acquire up to N distinct nearest enemies within FinalRange.
            // Zero found → cancel this cast cleanly. Surplus (Amount > found) is fanned from the caster.
            NativeList<float3> multiTargets = default;
            int multiTargetsFound = 0;
            if (multiCast == ESpellMultiCast.MultiTarget && isPlayerCaster)
            {
                multiTargets = new NativeList<float3>(totalCount, Allocator.Temp);
                var mtFilter = new CollisionFilter
                {
                    BelongsTo = CollisionLayers.Raycast,
                    CollidesWith = CollisionLayers.Enemy
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

            // Spawn loop
            for (int i = 0; i < totalCount; i++)
            {
                var spellEntity = ECB.Instantiate(chunkIndex, spellPrefab);

                ECB.SetComponent(chunkIndex, spellEntity, new SpellSource
                {
                    CasterEntity = request.Caster,
                    DatabaseIndex = request.DatabaseIndex
                });

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
                    if (AreaAttackLookup.HasComponent(spellPrefab)
                        && !CopyPositionLookup.HasComponent(spellPrefab) && !isAttached)
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

                ECB.SetComponent(chunkIndex, spellEntity, new LocalTransform
                {
                    Position = spawnPos,
                    Rotation = finalRotation,
                    Scale = spellPrefabTransform.Scale * finalSize // todo fix it
                });

                // Movement
                if (LinearMovementLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponent(chunkIndex, spellEntity, new LinearMovement
                    {
                        Direction = finalDirection,
                        Speed = finalSpeed
                    });
                }

                if (FollowMovementLookup.HasComponent(spellPrefab) && targetEntity != Entity.Null)
                {
                    ECB.SetComponent(chunkIndex, spellEntity, new FollowTargetMovement
                    {
                        Target = targetEntity,
                        Speed = finalSpeed
                    });
                }

                if (OrbitMovementLookup.HasComponent(spellPrefab))
                {
                    float orbitRadius =
                        math.length(baseSpellData.BaseSpawnOffset) *
                        finalSize;
                    ECB.SetComponent(chunkIndex, spellEntity, new OrbitMovement
                    {
                        OrbitCenterEntity = request.Caster,
                        Radius = orbitRadius,
                        AngularSpeed = finalSpeed,
                        RelativeOffset = new float3(0, 0, orbitRadius),
                        OrbitCenterPosition = casterTransform.Position
                    });
                }

                if (AttachLookup.HasComponent(spellPrefab))
                {
                    ECB.AddComponent(chunkIndex, spellEntity, new Parent { Value = request.Caster });
                    ECB.SetComponent(chunkIndex, spellEntity, new LocalTransform
                    {
                        // Parent-local offset: caster-local x/y/z relative to the caster transform.
                        Position = baseSpellData.BaseSpawnOffset,
                        Rotation = quaternion.identity,
                        Scale = finalSize
                    });
                }

                if (!AttachLookup.HasComponent(spellPrefab) && CopyPositionLookup.HasComponent(spellPrefab))
                {
                    var copyPos = CopyPositionLookup[spellPrefab];
                    copyPos.Target = request.Caster;
                    // Keep the spell offset from the caster every frame (rotated into caster space by the system).
                    copyPos.Offset = baseSpellData.BaseSpawnOffset;

                    ECB.SetComponent(chunkIndex, spellEntity, copyPos);
                }

                // Combat Stats
                if (DamageOnContactLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponent(chunkIndex, spellEntity, new DamageOnContact
                    {
                        Damage = finalDamage,
                        Tags = totalTags,
                        AreaRadius = finalSize,
                        TotalCritChance = activeSpell.FinalCritChance,
                        TotalCritMultiplier = activeSpell.FinalCritDamageMultiplier,
                        TargetLayers = filter.CollidesWith
                    });
                }

                // Area Attack (unified Burst + OverTime — Cadence is baked on the prefab)
                if (AreaAttackLookup.HasComponent(spellPrefab))
                {
                    var zone = AreaAttackLookup[spellPrefab]; // shape / cadence / timing from prefab
                    zone.Damage = finalDamage;
                    zone.CritChance = activeSpell.FinalCritChance;
                    zone.CritMultiplier = activeSpell.FinalCritDamageMultiplier;
                    zone.Caster = request.Caster;
                    zone.TargetLayers = filter.CollidesWith;
                    zone.Tags = totalTags;
                    zone.ElapsedTime = 0f;

                    if (zone.Cadence == EZoneCadence.OverTime)
                    {
                        float baseRadius = zone.PrefabRadius > 0f ? zone.PrefabRadius : 1f;
                        zone.PrefabRadius = baseRadius;
                        zone.RadiusStart = finalSize * baseRadius;
                        zone.RadiusEnd = zone.RadiusStart;
                        zone.TickRate = finalTickRate;
                        ECB.AddBuffer<TickDamageTarget>(chunkIndex, spellEntity);
                    }
                    else // Burst
                    {
                        zone.RadiusStart = finalSize * zone.RadiusStart;
                        zone.RadiusEnd = finalSize * zone.RadiusEnd;
                    }

                    ECB.SetComponent(chunkIndex, spellEntity, zone);
                }

                // Lifetime
                if (LifetimeLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponent(chunkIndex, spellEntity, new Lifetime
                    {
                        Duration = finalDuration,
                        TimeLeft = finalDuration
                    });
                }

                // Self Rotate
                if (SelfRotateLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponent(chunkIndex, spellEntity, new SelfRotate
                    {
                        RotationSpeed = finalSpeed
                    });
                }

                // Sub Spell Spawner
                if (SubSpellsSpawnerLookup.HasComponent(spellPrefab))
                {
                    if (baseSpellData.ChildPrefabIndex >= 0 &&
                        baseSpellData.ChildPrefabIndex < ChildSpellPrefabs.Length)
                    {
                        SubSpellsSpawner childSpawnerData = SubSpellsSpawnerLookup[spellPrefab];
                        childSpawnerData.ChildEntityPrefab = ChildSpellPrefabs[baseSpellData.ChildPrefabIndex].Prefab;
                        childSpawnerData.DesiredSubSpellsCount = finalAmount;
                        childSpawnerData.IsDirty = true;
                        childSpawnerData.CollisionFilter = filter;

                        ECB.SetComponent(chunkIndex, spellEntity, childSpawnerData);

                        if (SubSpellsCircleLayoutLookup.HasComponent(spellPrefab))
                        {
                            ECB.SetComponent(chunkIndex, spellEntity, new SubSpellsLayout_Circle
                            {
                                Radius = baseSpellData.ChildrenSpawnRadius,
                                AngleInDegrees = 360
                            });
                        }
                    }
                }

                // Bounce
                bool forceBounce = (totalTags & ESpellTag.Bouncing) != 0;
                if ((activeSpell.FinalBounces > 0 || forceBounce) && BounceLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponentEnabled<Bounce>(chunkIndex, spellEntity, true);
                    ECB.SetComponent(chunkIndex, spellEntity, new Bounce
                    {
                        RemainingBounces = finalBounces,
                        BounceRange = finalBounceRange,
                        BounceSpeed = finalSpeed
                    });
                }

                // Pierce
                bool forcePierce = (totalTags & ESpellTag.Piercing) != 0;
                if ((finalPierce > 0 || forcePierce) && PierceLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponentEnabled<Pierce>(chunkIndex, spellEntity, true);
                    ECB.SetComponent(chunkIndex, spellEntity, new Pierce { RemainingPierces = finalPierce });
                }

                // Explosion
                bool forceExplode = (totalTags & ESpellTag.Explosive) != 0;

                // Explose on contact
                if (forceExplode && ExplodeOnContactLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponentEnabled<ExplodeOnContact>(chunkIndex, spellEntity, true);
                    var explosion = ExplodeOnContactLookup[spellPrefab];
                    explosion.Damage += finalDamage * 0.5f; // todo explosion damage multiplier on stats
                    explosion.Radius *= finalSize;
                    ECB.SetComponent(chunkIndex, spellEntity, explosion);
                }

                //todo Explose on death or directly on prefab
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