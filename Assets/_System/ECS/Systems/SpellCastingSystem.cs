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

    private BufferLookup<ActiveSpell> _activeSpellLookup;

    // Behaviors
    private ComponentLookup<Lifetime> _lifetimeLookup;
    private ComponentLookup<PhysicsCollider> _colliderLookup;
    private ComponentLookup<AttachToCaster> _attachLookup;
    private ComponentLookup<CopyEntityPosition> _copyPositionLookup;
    private ComponentLookup<SelfRotate> _selfRotateLookup;

    private ComponentLookup<DamageOnContact> _damageOnContactLookup;
    private ComponentLookup<DamageOnTick> _damageOnTickLookup;

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
        state.RequireForUpdate<SpellPrefab>();
        state.RequireForUpdate<ActiveSpell>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<CastSpellRequest>();
        state.RequireForUpdate<PhysicsWorldSingleton>();

        _playerLookup = SystemAPI.GetComponentLookup<Player>(true);
        _enemyLookup = SystemAPI.GetComponentLookup<Enemy>(true);
        _transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        _activeSpellLookup = SystemAPI.GetBufferLookup<ActiveSpell>(true);
        _lifetimeLookup = SystemAPI.GetComponentLookup<Lifetime>(true);
        _colliderLookup = SystemAPI.GetComponentLookup<PhysicsCollider>(true);
        _attachLookup = SystemAPI.GetComponentLookup<AttachToCaster>(true);
        _copyPositionLookup = SystemAPI.GetComponentLookup<CopyEntityPosition>(true);
        _selfRotateLookup = SystemAPI.GetComponentLookup<SelfRotate>(true);
        _damageOnContactLookup = SystemAPI.GetComponentLookup<DamageOnContact>(true);
        _damageOnTickLookup = SystemAPI.GetComponentLookup<DamageOnTick>(true);
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
        _activeSpellLookup.Update(ref state);

        _lifetimeLookup.Update(ref state);
        _colliderLookup.Update(ref state);
        _attachLookup.Update(ref state);
        _copyPositionLookup.Update(ref state);
        _selfRotateLookup.Update(ref state);
        _damageOnContactLookup.Update(ref state);
        _damageOnTickLookup.Update(ref state);
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

        var castJob = new CastSpellJob
        {
            ECB = ecb.AsParallelWriter(),
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1,
            CollisionWorld = physicsWorldSingleton.CollisionWorld,
            SpellDatabaseRef = spellDatabase.Blobs,
            MainSpellPrefabs = mainSpellPrefabs,
            ChildSpellPrefabs = childSpellPrefabs,

            PlayerLookup = _playerLookup,
            EnemyLookup = _enemyLookup,
            TransformLookup = _transformLookup,
            ActiveSpellLookup = _activeSpellLookup,

            LifetimeLookup = _lifetimeLookup,
            ColliderLookup = _colliderLookup,
            AttachLookup = _attachLookup,
            CopyPositionLookup = _copyPositionLookup,
            SelfRotateLookup = _selfRotateLookup,
            DamageOnContactLookup = _damageOnContactLookup,
            DamageOnTickLookup = _damageOnTickLookup,
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

        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public DynamicBuffer<SpellPrefab> MainSpellPrefabs;
        [ReadOnly] public DynamicBuffer<ChildSpellPrefab> ChildSpellPrefabs;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;

        [ReadOnly] public ComponentLookup<Lifetime> LifetimeLookup;
        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;
        [ReadOnly] public ComponentLookup<AttachToCaster> AttachLookup;
        [ReadOnly] public ComponentLookup<CopyEntityPosition> CopyPositionLookup;
        [ReadOnly] public ComponentLookup<SelfRotate> SelfRotateLookup;
        [ReadOnly] public ComponentLookup<DamageOnContact> DamageOnContactLookup;
        [ReadOnly] public ComponentLookup<DamageOnTick> DamageOnTickLookup;
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
            float finalArea = activeSpell.FinalArea;
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


            // Multishot Logic (Spawners cast exactly 1 entity)
            int finalProjectileCount = math.max(1, finalAmount);
            if (SubSpellsSpawnerLookup.HasComponent(spellPrefab))
                finalProjectileCount = 1;

            // Targeting
            float3 targetPosition = casterTransform.Position;
            bool targetFound = false;
            Entity targetEntity = Entity.Null;
            bool isPlayerCaster = PlayerLookup.HasComponent(request.Caster);

            float3 baseSpawnPos =
                casterTransform.Position + (casterTransform.Forward() * baseSpellData.BaseSpawnOffset);
            quaternion baseRotation = casterTransform.Rotation;
            float3 fireDirection = casterTransform.Forward();

            float3 planetCenter = float3.zero; // todo use reel planet center from singleton

            var filter = new CollisionFilter
            {
                BelongsTo = isPlayerCaster ? CollisionLayers.PlayerSpell : CollisionLayers.EnemySpell,
                CollidesWith = (isPlayerCaster ? CollisionLayers.Enemy : CollisionLayers.Player) |
                               CollisionLayers.Obstacle,
            };

            switch (baseSpellData.TargetingMode)
            {
                case ESpellTargetingMode.OnCaster:
                    targetPosition = casterTransform.Position;
                    targetEntity = request.Caster;
                    targetFound = true;
                    break;

                case ESpellTargetingMode.CastForward:
                    targetPosition = casterTransform.Position +
                                     (casterTransform.Forward() * baseSpellData.BaseCastRange);
                    targetFound = true;
                    break;

                case ESpellTargetingMode.NearestTarget:
                    PointDistanceInput input = new PointDistanceInput
                    {
                        Position = casterTransform.Position,
                        MaxDistance = baseSpellData.BaseCastRange,
                        Filter = new CollisionFilter
                        {
                            BelongsTo = CollisionLayers.Raycast,
                            CollidesWith = isPlayerCaster ? CollisionLayers.Enemy : CollisionLayers.Player,
                        }
                    };
                    if (CollisionWorld.CalculateDistance(input, out DistanceHit hit))
                    {
                        targetEntity = hit.Entity;
                        targetPosition = TransformLookup.HasComponent(hit.Entity)
                            ? TransformLookup[hit.Entity].Position
                            : hit.Position;
                        targetFound = true;
                    }
                    else
                    {
                        // Fallback 
                        ECB.DestroyEntity(chunkIndex, requestEntity);
                        return;
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
                            baseSpellData.BaseCastRange,
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
                if (targetFound && isProjectile)
                {
                    float3 toTarget = targetPosition - baseSpawnPos;
                    if (math.lengthsq(toTarget) > math.EPSILON)
                    {
                        fireDirection = math.normalize(toTarget);
                        baseRotation = quaternion.LookRotationSafe(fireDirection, surfaceNormal);
                    }
                }
                else if (!isProjectile)
                {
                    baseSpawnPos = targetPosition; // Area spells spawn on target
                    baseRotation = quaternion.LookRotationSafe(casterTransform.Forward(), surfaceNormal);
                }
            }

            // Spawn loop
            float spreadAngle = 15f;
            float startAngle = -((finalProjectileCount - 1) * spreadAngle) / 2f;

            for (int i = 0; i < finalProjectileCount; i++)
            {
                var spellEntity = ECB.Instantiate(chunkIndex, spellPrefab);

                ECB.AddComponent(0, spellEntity, new RunScope()); // todo authoring
                ECB.AddComponent(chunkIndex, spellEntity, new SpellSource // todo authoring
                {
                    CasterEntity = request.Caster,
                    DatabaseIndex = request.DatabaseIndex
                });

                // Spread 
                quaternion finalRotation = baseRotation;
                float3 finalDirection = fireDirection;

                if (finalProjectileCount > 1 && isProjectile)
                {
                    float angle = startAngle + (i * spreadAngle);
                    finalRotation = math.mul(baseRotation, quaternion.RotateY(math.radians(angle)));
                    finalDirection = math.forward(finalRotation);
                }

                ECB.SetComponent(chunkIndex, spellEntity, new LocalTransform
                {
                    Position = baseSpawnPos,
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
                        finalArea; // todo Orbit radius scales with projectile size
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
                        Position = float3.zero,
                        Rotation = quaternion.identity,
                        Scale = finalArea
                    });
                }

                if (!AttachLookup.HasComponent(spellPrefab) && CopyPositionLookup.HasComponent(spellPrefab))
                {
                    var copyPos = CopyPositionLookup[spellPrefab];
                    copyPos.Target = request.Caster;

                    ECB.SetComponent(chunkIndex, spellEntity, copyPos);
                }

                // Combat Stats
                if (DamageOnContactLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponent(chunkIndex, spellEntity, new DamageOnContact
                    {
                        Damage = finalDamage,
                        Tag = totalTags,
                        AreaRadius = finalArea,
                        TotalCritChance = activeSpell.FinalCritChance,
                        TotalCritMultiplier = activeSpell.FinalCritDamageMultiplier
                    });
                }

                if (DamageOnTickLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponent(chunkIndex, spellEntity, new DamageOnTick
                    {
                        Caster = request.Caster,
                        TickRate = finalTickRate,
                        DamagePerTick = finalDamage,
                        AreaRadius = finalArea,
                        Element = totalTags,
                        TotalCritChance = finalCritChance,
                        TotalCritMultiplier = finalCritDamageMultiplier
                    });
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
                        childSpawnerData.DesiredSubSpellsCount = finalAmount; // Injecting Final Amount here 
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

                // Collision 
                if (ColliderLookup.HasComponent(spellPrefab))
                {
                    var col = ColliderLookup[spellPrefab];
                    col.Value.Value.SetCollisionFilter(filter);
                    ECB.SetComponent(chunkIndex, spellEntity, col);
                }

                // Enableable components (Upgrades)

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
                    explosion.Radius *= finalArea;
                    ECB.SetComponent(chunkIndex, spellEntity, explosion);
                }

                //todo Explose on death
            }

            ECB.DestroyEntity(chunkIndex, requestEntity);
        }
    }
}