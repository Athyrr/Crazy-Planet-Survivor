//using Unity.Burst;
//using Unity.Collections;
//using Unity.Entities;
//using Unity.Mathematics;
//using Unity.Physics;
//using Unity.Transforms;

///// <summary>
///// The spell instantiation system. 
///// This system processes <see cref="CastSpellRequest"/> entities, calculates targeting/spawn parameters 
///// based on the spell database, and initializes the resulting spell entities with appropriate components.
///// </summary>
//[UpdateInGroup(typeof(SimulationSystemGroup))]
//[BurstCompile]
//public partial struct SpellCastingSystem : ISystem
//{
//    private ComponentLookup<Player> _playerLookup;
//    private ComponentLookup<Enemy> _enemyLookup;

//    private ComponentLookup<LocalTransform> _transformLookup;
//    private BufferLookup<ActiveSpell> _activeSpellLookup;
//    private ComponentLookup<Stats> _statsLookup;
//    private ComponentLookup<Lifetime> _lifetimeLookup;
//    private ComponentLookup<PhysicsCollider> _colliderLookup;
//    private ComponentLookup<AttachToCaster> _attachLookup;
//    private ComponentLookup<CopyEntityPosition> _copyPositionLookup;
//    private ComponentLookup<SelfRotate> _selfRotateLookup;
//    private ComponentLookup<DamageOnContact> _damageOnContactLookup;
//    private ComponentLookup<DamageOnTick> _damageOnTickLookup;
//    private ComponentLookup<LinearMovement> _linearMovementLookup;
//    private ComponentLookup<OrbitMovement> _orbitMovementLookup;
//    private ComponentLookup<FollowTargetMovement> _followMovementLookup;
//    private ComponentLookup<ChildEntitiesSpawner> _childSpawnerLookup;
//    private ComponentLookup<ChildEntitiesLayout_Circle> _childCircleLayoutLookup;
//    private ComponentLookup<Ricochet> _ricochetLookup;
//    private ComponentLookup<Pierce> _pierceLookup;
//    //  private ComponentLookup<ExplodeOnContact> _explodeLookup;

//    [BurstCompile]
//    public void OnCreate(ref SystemState state)
//    {
//        // Ensure all necessary data structures and singletons are available
//        state.RequireForUpdate<Stats>();
//        state.RequireForUpdate<SpellPrefab>();
//        state.RequireForUpdate<ActiveSpell>();
//        state.RequireForUpdate<SpellsDatabase>();
//        state.RequireForUpdate<CastSpellRequest>();
//        state.RequireForUpdate<PhysicsWorldSingleton>();

//        _playerLookup = SystemAPI.GetComponentLookup<Player>(true);
//        _enemyLookup = SystemAPI.GetComponentLookup<Enemy>(true);

//        _transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
//        _statsLookup = SystemAPI.GetComponentLookup<Stats>(true);
//        _activeSpellLookup = SystemAPI.GetBufferLookup<ActiveSpell>(true);
//        _lifetimeLookup = SystemAPI.GetComponentLookup<Lifetime>(true);
//        _colliderLookup = SystemAPI.GetComponentLookup<PhysicsCollider>(true);

//        _attachLookup = SystemAPI.GetComponentLookup<AttachToCaster>(true);
//        _copyPositionLookup = SystemAPI.GetComponentLookup<CopyEntityPosition>(true);
//        _selfRotateLookup = SystemAPI.GetComponentLookup<SelfRotate>(true);

//        _damageOnContactLookup = SystemAPI.GetComponentLookup<DamageOnContact>(true);
//        _damageOnTickLookup = SystemAPI.GetComponentLookup<DamageOnTick>(true);

//        _linearMovementLookup = SystemAPI.GetComponentLookup<LinearMovement>(true);
//        _orbitMovementLookup = SystemAPI.GetComponentLookup<OrbitMovement>(true);
//        _followMovementLookup = SystemAPI.GetComponentLookup<FollowTargetMovement>(true);

//        _childSpawnerLookup = SystemAPI.GetComponentLookup<ChildEntitiesSpawner>(true);
//        _childCircleLayoutLookup = SystemAPI.GetComponentLookup<ChildEntitiesLayout_Circle>(true);

//        _ricochetLookup = SystemAPI.GetComponentLookup<Ricochet>(true);
//        _pierceLookup = SystemAPI.GetComponentLookup<Pierce>(true);
//        // _explodeLookup = SystemAPI.GetComponentLookup<ExplodeOnContact>(true);
//    }

//    [BurstCompile]
//    public void OnUpdate(ref SystemState state)
//    {
//        // Only process casting if the game is in the 'Running' state
//        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
//            return;

//        if (gameState.State != EGameState.Running)
//            return;

//        // Update component lookups
//        _playerLookup.Update(ref state);
//        _enemyLookup.Update(ref state);
//        _activeSpellLookup.Update(ref state);
//        _transformLookup.Update(ref state);
//        _statsLookup.Update(ref state);
//        _lifetimeLookup.Update(ref state);
//        _colliderLookup.Update(ref state);
//        _attachLookup.Update(ref state);
//        _copyPositionLookup.Update(ref state);
//        _selfRotateLookup.Update(ref state);
//        _damageOnContactLookup.Update(ref state);
//        _damageOnTickLookup.Update(ref state);
//        _linearMovementLookup.Update(ref state);
//        _orbitMovementLookup.Update(ref state);
//        _followMovementLookup.Update(ref state);
//        _childSpawnerLookup.Update(ref state);
//        _childCircleLayoutLookup.Update(ref state);
//        _ricochetLookup.Update(ref state);
//        _pierceLookup.Update(ref state);
//        //_explodeLookup.Update(ref state);

//        // Initialize Command Buffer for structural changes (instantiation/destruction)
//        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
//        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

//        var spellDatabaseEntity = SystemAPI.GetSingletonEntity<SpellsDatabase>();

//        var mainSpellPrefabs = SystemAPI.GetSingletonBuffer<SpellPrefab>(true);
//        var childSpellPrefabs = SystemAPI.GetSingletonBuffer<ChildSpellPrefab>(true);
//        var spellDatabase = SystemAPI.GetComponent<SpellsDatabase>(spellDatabaseEntity);

//        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

//        // Configure the casting job with all necessary lookups to resolve targets and stats
//        var castJob = new CastSpellJob
//        {
//            ECB = ecb.AsParallelWriter(),
//            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1, // Generate a frame-based seed

//            CollisionWorld = physicsWorldSingleton.CollisionWorld,

//            SpellDatabaseRef = spellDatabase.Blobs,
//            BaseSpellPrefabs = mainSpellPrefabs,
//            ChildSpellPrefabs = childSpellPrefabs,

//            PlayerLookup = _playerLookup,
//            EnemyLookup = _enemyLookup,

//            TransformLookup = _transformLookup,
//            StatsLookup = _statsLookup,
//            LifetimeLookup = _lifetimeLookup,
//            ColliderLookup = _colliderLookup,

//            AttachLookup = _attachLookup,
//            CopyPositionLookup = _copyPositionLookup,
//            SelfRotateLookup = _selfRotateLookup,

//            DamageOnContactLookup = _damageOnContactLookup,
//            DamageOnTickLookup = _damageOnTickLookup,

//            LinearMovementLookup = _linearMovementLookup,
//            OrbitMovementLookup = _orbitMovementLookup,
//            FollowMovementLookup = _followMovementLookup,

//            ChildSpawnerLookup = _childSpawnerLookup,
//            ChildCircleLayoutLookup = _childCircleLayoutLookup,

//            RicochetLookup = _ricochetLookup,
//            PierceLookup = _pierceLookup,
//        };

//        state.Dependency = castJob.ScheduleParallel(state.Dependency);
//    }

//    /// <summary>
//    /// Processes individual spell requests. Handles targeting logic, spawn positioning, 
//    /// and component-wise initialization of the instantiated spell prefab.
//    /// </summary>
//    [BurstCompile]
//    private partial struct CastSpellJob : IJobEntity
//    {
//        public EntityCommandBuffer.ParallelWriter ECB;
//        public uint Seed;


//        [ReadOnly] public CollisionWorld CollisionWorld;

//        [ReadOnly] public DynamicBuffer<SpellPrefab> BaseSpellPrefabs;
//        [ReadOnly] public DynamicBuffer<ChildSpellPrefab> ChildSpellPrefabs;
//        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

//        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
//        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;

//        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
//        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;
//        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
//        [ReadOnly] public ComponentLookup<Lifetime> LifetimeLookup;
//        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;

//        [ReadOnly] public ComponentLookup<AttachToCaster> AttachLookup;
//        [ReadOnly] public ComponentLookup<CopyEntityPosition> CopyPositionLookup;
//        [ReadOnly] public ComponentLookup<SelfRotate> SelfRotateLookup;

//        [ReadOnly] public ComponentLookup<DamageOnContact> DamageOnContactLookup;
//        [ReadOnly] public ComponentLookup<DamageOnTick> DamageOnTickLookup;

//        [ReadOnly] public ComponentLookup<LinearMovement> LinearMovementLookup;
//        [ReadOnly] public ComponentLookup<OrbitMovement> OrbitMovementLookup;
//        [ReadOnly] public ComponentLookup<FollowTargetMovement> FollowMovementLookup;

//        [ReadOnly] public ComponentLookup<ChildEntitiesSpawner> ChildSpawnerLookup;
//        [ReadOnly] public ComponentLookup<ChildEntitiesLayout_Circle> ChildCircleLayoutLookup;

//        [ReadOnly] public ComponentLookup<Ricochet> RicochetLookup;
//        [ReadOnly] public ComponentLookup<Pierce> PierceLookup;

//        //[ReadOnly] public ComponentLookup<ExplodeOnContact> ExplodeLookup;

//        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity requestEntity, in CastSpellRequest request)
//        {
//            // Validations
//            if (!SpellDatabaseRef.IsCreated || !TransformLookup.HasComponent(request.Caster))
//            {
//                ECB.DestroyEntity(chunkIndex, requestEntity);
//                return;
//            }

//            ref readonly var baseSpellData = ref SpellDatabaseRef.Value.Spells[request.DatabaseIndex];
//            var spellPrefab = BaseSpellPrefabs[request.DatabaseIndex].Prefab;

//            if (spellPrefab == Entity.Null && baseSpellData.ChildPrefabIndex == -1) // -1 : default value for none
//            {
//                ECB.DestroyEntity(chunkIndex, requestEntity);
//                return;
//            }

//            // Fetch Active Spell Modifiers
//            float dmgMult = 1f;
//            float speedMult = 1f;
//            float areaMult = 1f;
//            float durationMult = 1f;
//            int bonusAmount = 0;
//            int bonusBounces = 0;
//            int bonusPierces = 0;

//            ESpellTag addedTags = ESpellTag.None;

//            if (ActiveSpellLookup.TryGetBuffer(request.Caster, out var activeSpells))
//            {
//                for (int i = 0; i < activeSpells.Length; i++)
//                {
//                    if (activeSpells[i].DatabaseIndex == request.DatabaseIndex)
//                    {
//                        var activeSpell = activeSpells[i];

//                        dmgMult = activeSpell.DamageMultiplier;
//                        speedMult = activeSpell.SpeedMultiplier;
//                        areaMult = activeSpell.AreaMultiplier;
//                        durationMult = activeSpell.DurationMultiplier;

//                        bonusAmount = activeSpell.BonusAmount;
//                        bonusBounces = activeSpell.BonusBounces;
//                        bonusPierces = activeSpell.BonusPierces;
//                        addedTags = activeSpell.AddedTags;
//                        break;
//                    }
//                }
//            }

//            // Context Setup
//            var caster = request.Caster;
//            var casterTransform = TransformLookup[caster];
//            bool isPlayerCaster = PlayerLookup.HasComponent(caster);
//            var stats = StatsLookup[caster];

//            // Set Collision layers
//            var filter = new CollisionFilter
//            {
//                BelongsTo = isPlayerCaster ? CollisionLayers.PlayerSpell : CollisionLayers.EnemySpell,
//                CollidesWith = (isPlayerCaster ? CollisionLayers.Enemy : CollisionLayers.Player) | CollisionLayers.Obstacle,
//            };

//            Entity targetEntity = Entity.Null;
//            float3 targetPosition = float3.zero;
//            bool targetFound = false;

//            var random = Random.CreateFromIndex(Seed);

//            // Calculate final spell stats with multipliers and bonuses
//            float finalDamage = (baseSpellData.BaseDamage + stats.Damage) * speedMult;
//            float finalSpeed = baseSpellData.BaseSpeed * math.max(1, stats.ProjectileSpeedMultiplier) * speedMult;
//            float finalArea = baseSpellData.BaseEffectArea * math.max(1, stats.EffectAreaRadiusMult) * areaMult;
//            int finalAmount = 1 + bonusAmount;

//            // Targeting Logic 
//            switch (baseSpellData.TargetingMode)
//            {
//                case ESpellTargetingMode.OnCaster:
//                    targetEntity = request.Caster;
//                    targetPosition = casterTransform.Position;
//                    targetFound = true;
//                    break;

//                case ESpellTargetingMode.CastForward:
//                    targetPosition = casterTransform.Position + (casterTransform.Forward() * baseSpellData.BaseSpawnOffset);
//                    targetFound = true;
//                    break;

//                case ESpellTargetingMode.Nearest:
//                    // Use Physics World to find the closest valid target within range
//                    PointDistanceInput input = new PointDistanceInput
//                    {
//                        Position = casterTransform.Position,
//                        MaxDistance = baseSpellData.BaseCastRange,
//                        Filter = filter
//                    };

//                    if (CollisionWorld.CalculateDistance(input, out DistanceHit hit))
//                    {
//                        targetEntity = hit.Entity;
//                        targetPosition = TransformLookup.HasComponent(hit.Entity) ? TransformLookup[hit.Entity].Position : hit.Position;

//                        targetFound = true;
//                    }
//                    break;

//                case ESpellTargetingMode.RandomInRange:
//                    // Find a random point on the planet surface within the cast radius
//                    var sphere = random.NextFloat3Direction() * random.NextFloat(0, baseSpellData.BaseCastRange);
//                    var tempPosition = casterTransform.Position + new float3(sphere.x, sphere.y, sphere.z);

//                    CollisionFilter planetFilter = new CollisionFilter()
//                    {
//                        BelongsTo = CollisionLayers.Raycast,
//                        CollidesWith = CollisionLayers.Landscape
//                    };

//                    //@todo use PlanetCenter instead of float.zero
//                    if (PlanetUtils.GetRandomPointOnSurface(
//                                ref CollisionWorld,
//                                ref random,
//                                casterTransform.Position,
//                                float3.zero, // //@todo Planet Center 
//                                baseSpellData.BaseCastRange,
//                              ref planetFilter,
//                                out float3 randomSurfacePos))
//                    {
//                        targetPosition = randomSurfacePos;
//                        targetFound = true;
//                    }
//                    else
//                    {
//                        targetPosition = casterTransform.Position;
//                        targetFound = true;
//                    }

//                    targetFound = true;
//                    break;
//            }

//            // Fallback: If "Nearest" targeting fails, aim forward by default
//            if (!targetFound && baseSpellData.TargetingMode == ESpellTargetingMode.Nearest)
//                targetPosition = casterTransform.Position + (casterTransform.Forward() * baseSpellData.BaseCastRange);


//            // Spawn
//            float3 spawnPosition = float3.zero;
//            quaternion spawnRotation = quaternion.identity;

//            bool isProjectile = LinearMovementLookup.HasComponent(spellPrefab);
//            bool spawnOnTarget = !isProjectile && !AttachLookup.HasComponent(spellPrefab) && !CopyPositionLookup.HasComponent(spellPrefab);

//            float3 fireDirection = float3.zero;

//            // Calculate surface alignment for the spawn point
//            float3 planetCenter = float3.zero;
//            float3 planetSurfaceNormal = math.normalize(targetPosition - planetCenter);

//            float3 fallbackForward = math.forward(casterTransform.Rotation);
//            PlanetUtils.ProjectDirectionOnSurface(fallbackForward, planetSurfaceNormal, out var planetTangentForward);

//            if (spawnOnTarget)
//            {
//                spawnPosition = targetPosition;
//                spawnRotation = TransformLookup.HasComponent(targetEntity) ? TransformLookup[targetEntity].Rotation : quaternion.LookRotationSafe(planetTangentForward, planetSurfaceNormal);
//            }
//            else
//            {
//                spawnPosition = casterTransform.Position + (casterTransform.Forward() * baseSpellData.BaseSpawnOffset);

//                if (targetFound && isProjectile)
//                {
//                    float3 vectorToTarget = targetPosition - spawnPosition;
//                    float toTargetDistSq = math.lengthsq(vectorToTarget);

//                    fireDirection = toTargetDistSq > math.EPSILON ? fireDirection = math.normalize(vectorToTarget) : casterTransform.Forward();
//                }
//                else
//                    fireDirection = casterTransform.Forward();

//                spawnRotation = TransformLookup.HasComponent(targetEntity) ? TransformLookup[targetEntity].Rotation : quaternion.identity;
//            }

//            float spreadAngle = 15f;
//            float startAngle = -((finalAmount - 1) * spreadAngle) / 2f;


//            // Spell Instantiation
//            var spellEntity = ECB.Instantiate(chunkIndex, spellPrefab);

//            // Components Configuration

//            ECB.SetComponent(chunkIndex, spellEntity, new LocalTransform
//            {
//                Position = spawnPosition,
//                Rotation = spawnRotation,
//                Scale = 1f
//            });

//            // If movement is Follow Target > Spawn on caster and follow target
//            if (targetFound && targetEntity != Entity.Null && FollowMovementLookup.HasComponent(spellPrefab) && FollowMovementLookup.IsComponentEnabled(spellPrefab))
//            {
//                ECB.SetComponent(chunkIndex, spellEntity, new FollowTargetMovement
//                {
//                    Target = targetEntity,
//                    Speed = baseSpellData.BaseSpeed * math.max(1, finalSpeed),
//                    StopDistance = 0
//                });
//            }

//            // If movement is Linear > Spawn on caster and go toward set direction
//            if (LinearMovementLookup.HasComponent(spellPrefab) && LinearMovementLookup.IsComponentEnabled(spellPrefab))
//            {
//                ECB.SetComponent(chunkIndex, spellEntity, new LinearMovement
//                {
//                    Direction = fireDirection,
//                    Speed = baseSpellData.BaseSpeed * math.max(1, finalSpeed)
//                });
//            }

//            // Set Orbit Movement if applicable
//            if (OrbitMovementLookup.HasComponent(spellPrefab) && OrbitMovementLookup.IsComponentEnabled(spellPrefab))
//            {
//                float radius = math.length(baseSpellData.BaseSpawnOffset);
//                float3 relativeOffset = new float3(0, 0, radius);

//                ECB.SetComponent(chunkIndex, spellEntity, new OrbitMovement
//                {
//                    OrbitCenterEntity = request.Caster,
//                    Radius = radius,
//                    RelativeOffset = relativeOffset,
//                    AngularSpeed = baseSpellData.BaseSpeed * math.max(1, finalSpeed),
//                    OrbitCenterPosition = casterTransform.Position
//                });
//            }

//            bool isAttached = AttachLookup.HasComponent(spellPrefab);
//            bool isCopingPosition = CopyPositionLookup.HasComponent(spellPrefab);

//            // Set Attach to Caster if applicable
//            if (isAttached)
//            {
//                ECB.AddComponent(chunkIndex, spellEntity, new Parent { Value = request.Caster });

//                spawnPosition = float3.zero;
//                spawnRotation = quaternion.identity;
//            }
//            // Set CopyPosition if applicable
//            else if (isCopingPosition)
//            {
//                var copyPosition = CopyPositionLookup[spellPrefab];
//                copyPosition.Target = request.Caster;

//                spawnPosition = casterTransform.Position + (casterTransform.Forward() * baseSpellData.BaseSpawnOffset);

//                float3 surfaceNormal = casterTransform.Up();
//                float3 forward = casterTransform.Forward();

//                PlanetUtils.ProjectDirectionOnSurface(forward, surfaceNormal, out var tangentForward);

//                spawnRotation = quaternion.LookRotationSafe(tangentForward, surfaceNormal);

//                ECB.SetComponent(chunkIndex, spellEntity, copyPosition);

//            }

//            if (SelfRotateLookup.HasComponent(spellPrefab))
//            {
//                ECB.SetComponent(chunkIndex, spellEntity, new SelfRotate
//                {
//                    RotationSpeed = baseSpellData.BaseSpeed * math.max(1, finalSpeed)
//                });
//            }

//            // Set Damage On Contact
//            if (DamageOnContactLookup.HasComponent(spellPrefab))
//            {
//                ECB.SetComponent(chunkIndex, spellEntity, new DamageOnContact
//                {
//                    Damage = baseSpellData.BaseDamage + finalDamage,
//                    Element = baseSpellData.Tag,
//                    AreaRadius = baseSpellData.BaseEffectArea * math.max(1, finalArea)
//                });
//            }

//            // Set Damage On Tick 
//            if (DamageOnTickLookup.HasComponent(spellPrefab))
//            {
//                ECB.SetComponent(chunkIndex, spellEntity, new DamageOnTick
//                {
//                    Caster = request.Caster,
//                    TickRate = baseSpellData.TickRate,
//                    DamagePerTick = baseSpellData.BaseDamagePerTick + finalDamage,
//                    ElapsedTime = 0f,
//                    AreaRadius = baseSpellData.BaseEffectArea * finalArea,
//                    Element = baseSpellData.Tag
//                });

//                // Scale the visual representation to match the effect area
//                ECB.SetComponent<LocalTransform>(chunkIndex, spellEntity, new LocalTransform
//                {
//                    Position = spawnPosition,
//                    Rotation = spawnRotation,
//                    Scale = baseSpellData.BaseEffectArea * finalArea * 2
//                });
//            }

//            // Set Lifetime
//            if (LifetimeLookup.HasComponent(spellPrefab))
//            {
//                ECB.SetComponent(chunkIndex, spellEntity, new Lifetime
//                {
//                    Duration = baseSpellData.Lifetime,
//                    TimeLeft = baseSpellData.Lifetime
//                });
//            }

//            // Physics Collider
//            if (ColliderLookup.HasComponent(spellPrefab))
//            {
//                var collider = ColliderLookup[spellPrefab];
//                collider.Value.Value.SetCollisionFilter(filter);
//                ECB.SetComponent(chunkIndex, spellEntity, collider);
//            }

//            // Set Child Entities Spawner if applicable
//            if (ChildSpawnerLookup.HasComponent(spellPrefab))
//            {
//                if (baseSpellData.ChildPrefabIndex >= 0 && baseSpellData.ChildPrefabIndex < ChildSpellPrefabs.Length)
//                {
//                    var childPrefabEntity = ChildSpellPrefabs[baseSpellData.ChildPrefabIndex].Prefab;

//                    ECB.SetComponent(chunkIndex, spellEntity, new ChildEntitiesSpawner
//                    {
//                        ChildEntityPrefab = childPrefabEntity,
//                        DesiredChildrenCount = baseSpellData.ChildrenCount,
//                        CollisionFilter = filter,
//                        IsDirty = true
//                    });
//                }

//                // Set Child Circle Layout if applicable
//                if (ChildCircleLayoutLookup.HasComponent(spellPrefab))
//                {
//                    ECB.SetComponent(chunkIndex, spellEntity, new ChildEntitiesLayout_Circle
//                    {
//                        Radius = baseSpellData.ChildrenSpawnRadius,
//                        AngleInDegrees = 360
//                    });
//                }
//            }

//            // Set Ricochet if applicable
//            if (RicochetLookup.HasComponent(spellPrefab))
//            {
//                int bouncesCount = baseSpellData.Bounces + stats.BouncesAdded;
//                ECB.SetComponent(chunkIndex, spellEntity, new Ricochet
//                {
//                    RemainingBounces = bouncesCount,
//                    BounceRange = math.max(1, baseSpellData.BouncesSearchRadius),
//                    BounceSpeed = baseSpellData.BaseSpeed * math.max(1, stats.ProjectileSpeedMultiplier),
//                });
//            }

//            // Set Pierce if applicable
//            if (PierceLookup.HasComponent(spellPrefab))
//            {
//                int pierceCount = baseSpellData.Pierces + stats.PierceAdded;
//                ECB.SetComponent(chunkIndex, spellEntity, new Pierce
//                {
//                    RemainingPierces = math.max(1, pierceCount)
//                });
//            }

//            // Cleanup the request entity now that the spell is spawned
//            ECB.DestroyEntity(chunkIndex, requestEntity);
//        }
//    }
//}


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
            // -----------------------------------------------------------------------------------------
            // VALIDATION
            // -----------------------------------------------------------------------------------------
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

            // -----------------------------------------------------------------------------------------
            // FETCH MODIFIERS (RECIPE)
            // -----------------------------------------------------------------------------------------
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

            // -----------------------------------------------------------------------------------------
            // CALCULATE FINAL STATS (Base + Stats + Upgrade)
            // -----------------------------------------------------------------------------------------
            var casterTransform = TransformLookup[request.Caster];
            var stats = StatsLookup[request.Caster];
            var random = Random.CreateFromIndex(Seed);

            float finalDamage = (baseSpellData.BaseDamage + stats.Damage) * mulDmg;
            float finalSpeed = baseSpellData.BaseSpeed * math.max(1, stats.ProjectileSpeedMultiplier) * mulSpeed;
            float finalArea = baseSpellData.BaseEffectArea * math.max(1, stats.EffectAreaRadiusMult) * mulArea;
            float finalDuration = baseSpellData.Lifetime * mulDuration;

            // Multishot Logic
            int finalProjectileCount = math.max(1, 1 + addAmount);

            // -----------------------------------------------------------------------------------------
            // TARGETING LOGIC (Determines Base Target Position/Rotation)
            // -----------------------------------------------------------------------------------------
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
                case ESpellTargetingMode.Nearest:
                    PointDistanceInput input = new PointDistanceInput
                    {
                        Position = casterTransform.Position,
                        MaxDistance = baseSpellData.BaseCastRange,
                        Filter = filter
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

            // -----------------------------------------------------------------------------------------
            // SPAWN CALCULATION (Position & Rotation Basis)
            // -----------------------------------------------------------------------------------------
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

            // -----------------------------------------------------------------------------------------
            // SPAWN LOOP (MULTISHOT)
            // -----------------------------------------------------------------------------------------
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
                    Scale = finalArea // Area acts as Scale
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
                    ECB.SetComponent(chunkIndex, spellEntity, new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = finalArea });
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