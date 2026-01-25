using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct SpellCastingSystem : ISystem
{
    // --- LOOKUPS ---
    // Caster info
    private ComponentLookup<Player> _playerLookup;
    private ComponentLookup<Enemy> _enemyLookup;
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<Stats> _statsLookup;

    // Spell "Recipe"
    private BufferLookup<ActiveSpell> _activeSpellLookup;

    // Spell Components (Read/Write to Prefab instance)
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

    private ComponentLookup<ChildEntitiesSpawner> _childSpawnerLookup;
    private ComponentLookup<ChildEntitiesLayout_Circle> _childCircleLayoutLookup;

    // Enableable Components
    private ComponentLookup<Ricochet> _ricochetLookup;
    private ComponentLookup<Pierce> _pierceLookup;
    //private ComponentLookup<ExplodeOnContact> _explodeLookup;


    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Stats>();
        state.RequireForUpdate<SpellPrefab>();
        state.RequireForUpdate<ActiveSpell>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<CastSpellRequest>();
        state.RequireForUpdate<PhysicsWorldSingleton>();

        _playerLookup = SystemAPI.GetComponentLookup<Player>(true);
        _enemyLookup = SystemAPI.GetComponentLookup<Enemy>(true);
        _transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        _statsLookup = SystemAPI.GetComponentLookup<Stats>(true);
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
        _childSpawnerLookup = SystemAPI.GetComponentLookup<ChildEntitiesSpawner>(true);
        _childCircleLayoutLookup = SystemAPI.GetComponentLookup<ChildEntitiesLayout_Circle>(true);
        _ricochetLookup = SystemAPI.GetComponentLookup<Ricochet>(true);
        _pierceLookup = SystemAPI.GetComponentLookup<Pierce>(true);
        //_explodeLookup = SystemAPI.GetComponentLookup<ExplodeOnContact>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        // Update all lookups
        _playerLookup.Update(ref state);
        _enemyLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _statsLookup.Update(ref state);
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
        _childSpawnerLookup.Update(ref state);
        _childCircleLayoutLookup.Update(ref state);
        _ricochetLookup.Update(ref state);
        _pierceLookup.Update(ref state);
        //_explodeLookup.Update(ref state);

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
            StatsLookup = _statsLookup,
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
            ChildSpawnerLookup = _childSpawnerLookup,
            ChildCircleLayoutLookup = _childCircleLayoutLookup,
            RicochetLookup = _ricochetLookup,
            PierceLookup = _pierceLookup,
            //ExplodeLookup = _explodeLookup
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
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
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
        [ReadOnly] public ComponentLookup<ChildEntitiesSpawner> ChildSpawnerLookup;
        [ReadOnly] public ComponentLookup<ChildEntitiesLayout_Circle> ChildCircleLayoutLookup;
        [ReadOnly] public ComponentLookup<Ricochet> RicochetLookup;
        [ReadOnly] public ComponentLookup<Pierce> PierceLookup;
        //[ReadOnly] public ComponentLookup<ExplodeOnContact> ExplodeLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity requestEntity, in CastSpellRequest request)
        {
            // VALIDATION
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


            // Default values (No modifiers)
            float mulDmg = 1f, mulSpeed = 1f, mulArea = 1f, mulDuration = 1f;
            int addAmount = 0, addBounces = 0, addPierces = 0;
            ESpellTag addedTags = ESpellTag.None;

            // Try to find the ActiveSpell config on the caster (Player only usually)
            if (ActiveSpellLookup.TryGetBuffer(request.Caster, out var activeSpells))
            {
                for (int i = 0; i < activeSpells.Length; i++)
                {
                    if (activeSpells[i].DatabaseIndex == request.DatabaseIndex)
                    {
                        var mod = activeSpells[i];
                        mulDmg = mod.DamageMultiplier;
                        mulSpeed = mod.SpeedMultiplier;
                        mulArea = mod.AreaMultiplier;
                        mulDuration = mod.DurationMultiplier;
                        addAmount = mod.BonusAmount;
                        addBounces = mod.BonusBounces;
                        addPierces = mod.BonusPierces;
                        addedTags = mod.AddedTags;
                        break;
                    }
                }
            }

            // CALCULATE FINAL STATS (Base + Stats + Upgrade)
            var casterTransform = TransformLookup[request.Caster];
            var spellPrefabTransform = TransformLookup[spellPrefab];
            var stats = StatsLookup[request.Caster];
            var random = Random.CreateFromIndex(Seed);

            float finalDamage = (baseSpellData.BaseDamage + stats.Damage) * mulDmg;
            float finalSpeed = baseSpellData.BaseSpeed * math.max(1f, stats.ProjectileSpeedMultiplier) * mulSpeed;
            float finalArea = baseSpellData.BaseEffectArea * math.max(1f, stats.EffectAreaRadiusMult) * mulArea;
            float finalDuration = baseSpellData.Lifetime * mulDuration;

            // Multishot Logic
            int finalProjectileCount = math.max(1, 1 + addAmount);

            // TARGETING LOGIC (Determines Base Target Position/Rotation)
            float3 targetPosition = casterTransform.Position;
            bool targetFound = false;
            Entity targetEntity = Entity.Null;
            bool isPlayerCaster = PlayerLookup.HasComponent(request.Caster);

            var filter = new CollisionFilter
            {
                BelongsTo = isPlayerCaster ? CollisionLayers.PlayerSpell : CollisionLayers.EnemySpell,
                CollidesWith = (isPlayerCaster ? CollisionLayers.Enemy : CollisionLayers.Player) | CollisionLayers.Obstacle,
            };

            switch (baseSpellData.TargetingMode)
            {
                case ESpellTargetingMode.OnCaster:
                    targetPosition = casterTransform.Position;
                    targetEntity = request.Caster;
                    targetFound = true;
                    break;
                case ESpellTargetingMode.CastForward:
                    targetPosition = casterTransform.Position + (casterTransform.Forward() * baseSpellData.BaseCastRange);
                    targetFound = true;
                    break;
                case ESpellTargetingMode.NearestTarget:
                    PointDistanceInput input = new PointDistanceInput
                    {
                        Position = casterTransform.Position,
                        MaxDistance = baseSpellData.BaseCastRange,
                        //Filter = filter
                        Filter = new CollisionFilter
                        {
                            BelongsTo = CollisionLayers.Raycast,
                            CollidesWith = isPlayerCaster ? CollisionLayers.Enemy : CollisionLayers.Player,
                        }
                    };
                    if (CollisionWorld.CalculateDistance(input, out DistanceHit hit))
                    {
                        targetEntity = hit.Entity;
                        targetPosition = TransformLookup.HasComponent(hit.Entity) ? TransformLookup[hit.Entity].Position : hit.Position;
                        targetFound = true;
                    }
                    else
                    {
                        // Fallback Forward
                        targetPosition = casterTransform.Position + (casterTransform.Forward() * baseSpellData.BaseCastRange);
                    }
                    break;
                case ESpellTargetingMode.RandomInRange:
                    // Simple random on flat plane for now (Replace with PlanetUtils if needed)
                    float2 rndCircle = random.NextFloat2Direction() * random.NextFloat(0, baseSpellData.BaseCastRange);
                    targetPosition = casterTransform.Position + new float3(rndCircle.x, 0, rndCircle.y);
                    targetFound = true;
                    break;
            }

            // SPAWN CALCULATION (Position & Rotation Basis)
            float3 baseSpawnPos = casterTransform.Position + (casterTransform.Forward() * baseSpellData.BaseSpawnOffset);
            quaternion baseRotation = casterTransform.Rotation;
            float3 fireDirection = casterTransform.Forward();

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
                        baseRotation = quaternion.LookRotationSafe(fireDirection, math.up()); // Should use Surface Normal
                    }
                }
                else if (!isProjectile)
                {
                    baseSpawnPos = targetPosition; // Area spells spawn on target
                }
            }

            // SPAWN LOOP (MULTISHOT)
            float spreadAngle = 15f;
            float startAngle = -((finalProjectileCount - 1) * spreadAngle) / 2f;

            for (int i = 0; i < finalProjectileCount; i++)
            {
                var spellEntity = ECB.Instantiate(chunkIndex, spellPrefab);

                // --- A. Transform & Spread ---
                quaternion finalRotation = baseRotation;
                float3 finalDirection = fireDirection;

                if (finalProjectileCount > 1 && isProjectile)
                {
                    float angle = startAngle + (i * spreadAngle);
                    // Rotate around UP axis
                    finalRotation = math.mul(baseRotation, quaternion.RotateY(math.radians(angle)));
                    finalDirection = math.forward(finalRotation);
                }

                ECB.SetComponent(chunkIndex, spellEntity, new LocalTransform
                {
                    Position = baseSpawnPos,
                    Rotation = finalRotation,
                    Scale = spellPrefabTransform.Scale * finalArea // Area acts as Scale
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
                        Speed = finalSpeed,
                        StopDistance = 0
                    });
                }

                if (OrbitMovementLookup.HasComponent(spellPrefab))
                {
                    float orbitRadius = math.length(baseSpellData.BaseSpawnOffset) * mulArea;
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
                        Element = baseSpellData.Tag | addedTags,
                        AreaRadius = finalArea
                    });
                }

                if (DamageOnTickLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponent(chunkIndex, spellEntity, new DamageOnTick
                    {
                        Caster = request.Caster,
                        TickRate = baseSpellData.TickRate, // Could have TickRateMultiplier too
                        DamagePerTick = (baseSpellData.BaseDamagePerTick + stats.Damage) * mulDmg,
                        AreaRadius = finalArea,
                        Element = baseSpellData.Tag | addedTags
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

                // Child Spawner
                if (ChildSpawnerLookup.HasComponent(spellPrefab))
                {
                    if (baseSpellData.ChildPrefabIndex >= 0 && baseSpellData.ChildPrefabIndex < ChildSpellPrefabs.Length)
                    {
                        var childPrefabEntity = ChildSpellPrefabs[baseSpellData.ChildPrefabIndex].Prefab;

                        ECB.SetComponent(chunkIndex, spellEntity, new ChildEntitiesSpawner
                        {
                            ChildEntityPrefab = childPrefabEntity,
                            DesiredChildrenCount = baseSpellData.ChildrenCount,
                            CollisionFilter = filter,
                            IsDirty = true // Trigger spawn in ChildEntitiesSpellSystem
                        });

                        // Config Circle Layout if applicable
                        if (ChildCircleLayoutLookup.HasComponent(spellPrefab))
                        {
                            ECB.SetComponent(chunkIndex, spellEntity, new ChildEntitiesLayout_Circle
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

                // ENABLEABLE MECHANICS (Upgrades)

                // Ricochet
                int totalBounces = baseSpellData.Bounces + stats.BouncesAdded + addBounces;
                bool forceBounce = (addedTags & ESpellTag.Bouncing) != 0;
                if ((totalBounces > 0 || forceBounce) && RicochetLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponentEnabled<Ricochet>(chunkIndex, spellEntity, true);
                    ECB.SetComponent(chunkIndex, spellEntity, new Ricochet
                    {
                        RemainingBounces = totalBounces,
                        BounceRange = baseSpellData.BouncesSearchRadius * mulArea,
                        BounceSpeed = finalSpeed
                    });
                }

                // Pierce
                int totalPierce = baseSpellData.Pierces + stats.PierceAdded + addPierces;
                bool forcePierce = (addedTags & ESpellTag.Piercing) != 0;
                if ((totalPierce > 0 || forcePierce) && PierceLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponentEnabled<Pierce>(chunkIndex, spellEntity, true);
                    ECB.SetComponent(chunkIndex, spellEntity, new Pierce { RemainingPierces = totalPierce });
                }

                // Explosion
                //bool forceExplode = (addedTags & ESpellTag.Explosive) != 0;
                //if (forceExplode && ExplodeLookup.HasComponent(spellPrefab))
                //{
                //    ECB.SetComponentEnabled<ExplodeOnContact>(chunkIndex, spellEntity, true);
                //    var exData = ExplodeLookup[spellPrefab];
                //    exData.Radius *= mulArea; // Scale explosion with area mod
                //    // @todo add Damage multiplier for explosion 
                //    ECB.SetComponent(chunkIndex, spellEntity, exData);
                //}
            }

            ECB.DestroyEntity(chunkIndex, requestEntity);
        }
    }
}