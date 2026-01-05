using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// The central orchestrator for spell instantiation. 
/// This system processes <see cref="CastSpellRequest"/> entities, calculates targeting/spawn parameters 
/// based on the spell database, and initializes the resulting spell entities with appropriate components.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct SpellCastingSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // Ensure all necessary data structures and singletons are available
        state.RequireForUpdate<Stats>();
        state.RequireForUpdate<SpellPrefab>();
        state.RequireForUpdate<ActiveSpell>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<CastSpellRequest>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Only process casting if the game is in the 'Running' state
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        // Initialize Command Buffer for structural changes (instantiation/destruction)
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var spellDatabaseEntity = SystemAPI.GetSingletonEntity<SpellsDatabase>();

        var mainSpellPrefabs = SystemAPI.GetSingletonBuffer<SpellPrefab>(true);
        var childSpellPrefabs = SystemAPI.GetSingletonBuffer<ChildSpellPrefab>(true);
        var spellDatabase = SystemAPI.GetComponent<SpellsDatabase>(spellDatabaseEntity);

        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        // Configure the casting job with all necessary lookups to resolve targets and stats
        var castJob = new CastSpellJob
        {
            ECB = ecb.AsParallelWriter(),
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1, // Generate a frame-based seed

            CollisionWorld = physicsWorldSingleton.CollisionWorld,
            
            SpellDatabaseRef = spellDatabase.Blobs,
            MainSpellPrefabs = mainSpellPrefabs,
            ChildSpellPrefabs = childSpellPrefabs,

            //@todo optimize lookups via caching in system (lookup.update)

            PlayerLookup = SystemAPI.GetComponentLookup<Player>(true),
            EnemyLookup = SystemAPI.GetComponentLookup<Enemy>(true),

            TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            StatsLookup = SystemAPI.GetComponentLookup<Stats>(true),
            LifetimeLookup = SystemAPI.GetComponentLookup<Lifetime>(true),
            ColliderLookup = SystemAPI.GetComponentLookup<PhysicsCollider>(true),

            AttachLookup = SystemAPI.GetComponentLookup<AttachToCaster>(true),
            CopyPositionLookup = SystemAPI.GetComponentLookup<CopyEntityPosition>(true),
            SelfRotateLookup = SystemAPI.GetComponentLookup<SelfRotate>(true),

            DamageOnContactLookup = SystemAPI.GetComponentLookup<DamageOnContact>(true),
            DamageOnTickLookup = SystemAPI.GetComponentLookup<DamageOnTick>(true),

            LinearMovementLookup = SystemAPI.GetComponentLookup<LinearMovement>(true),
            OrbitMovementLookup = SystemAPI.GetComponentLookup<OrbitMovement>(true),
            FollowMovementLookup = SystemAPI.GetComponentLookup<FollowTargetMovement>(true),

            ChildSpawnerLookup = SystemAPI.GetComponentLookup<ChildEntitiesSpawner>(true),
            ChildCircleLayoutLookup = SystemAPI.GetComponentLookup<ChildEntitiesLayout_Circle>(true),

            RicochetLookup = SystemAPI.GetComponentLookup<Ricochet>(true),
            PierceLookup = SystemAPI.GetComponentLookup<Pierce>(true),
        };

        state.Dependency = castJob.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// Processes individual spell requests. Handles targeting logic, spawn positioning, 
    /// and component-wise initialization of the instantiated spell prefab.
    /// </summary>
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
        
        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity requestEntity, in CastSpellRequest request)
        {
            // --- 1. Validation ---
            if (!SpellDatabaseRef.IsCreated || !TransformLookup.HasComponent(request.Caster))
            {
                ECB.DestroyEntity(chunkIndex, requestEntity);
                return;
            }

            ref readonly var spellData = ref SpellDatabaseRef.Value.Spells[request.DatabaseIndex];
            var spellPrefab = MainSpellPrefabs[request.DatabaseIndex].Prefab;

            if (spellPrefab == Entity.Null && spellData.ChildPrefabIndex == -1) // -1 : default value for none
            {
                ECB.DestroyEntity(chunkIndex, requestEntity);
                return;
            }

            // --- 2. Context Setup ---
            var caster = request.Caster;
            var casterTransform = TransformLookup[caster];
            bool isPlayerCaster = PlayerLookup.HasComponent(caster);

            // Define collision layers based on who is casting (Player vs Enemy)
            var filter = new CollisionFilter
            {
                BelongsTo = isPlayerCaster ? CollisionLayers.PlayerSpell : CollisionLayers.EnemySpell,
                CollidesWith = (isPlayerCaster ? CollisionLayers.Enemy : CollisionLayers.Player) | CollisionLayers.Obstacle,
            };

            Entity targetEntity = Entity.Null;
            float3 targetPosition = float3.zero; 
            bool targetFound = false;

            var random = Random.CreateFromIndex(Seed);

            // Fetch caster's dynamic stats to apply bonuses to the spell
            var stats = StatsLookup[caster];
            float bonusDamage = stats.Damage;
            float bonusSpellSpeedMult = stats.ProjectileSpeedMultiplier;
            float bonusAreaRadiusMult = math.max(1, stats.EffectAreaRadiusMult);

            // --- 3. Targeting Logic ---
             switch (spellData.TargetingMode)
            {
                case ESpellTargetingMode.OnCaster:
                    targetEntity = request.Caster;
                    targetPosition = casterTransform.Position;
                    targetFound = true;
                    break;

                case ESpellTargetingMode.CastForward:
                    targetPosition = casterTransform.Position + (casterTransform.Forward() * spellData.BaseSpawnOffset);
                    targetFound = true;
                    break;

                case ESpellTargetingMode.Nearest:
                    // Use Physics World to find the closest valid target within range
                    PointDistanceInput input = new PointDistanceInput
                    {
                        Position = casterTransform.Position,
                        MaxDistance = spellData.BaseCastRange,
                        Filter = filter
                    };

                    if (CollisionWorld.CalculateDistance(input, out DistanceHit hit))
                    {
                        targetEntity = hit.Entity;
                        targetPosition = TransformLookup.HasComponent(hit.Entity) ? TransformLookup[hit.Entity].Position : hit.Position;

                        targetFound = true;
                    }
                    break;

                case ESpellTargetingMode.RandomInRange:
                    // Find a random point on the planet surface within the cast radius
                    var sphere = random.NextFloat3Direction() * random.NextFloat(0, spellData.BaseCastRange);
                    var tempPosition = casterTransform.Position + new float3(sphere.x, sphere.y, sphere.z);

                    CollisionFilter planetFilter = new CollisionFilter()
                    {
                        BelongsTo = CollisionLayers.Raycast,
                        CollidesWith = CollisionLayers.Landscape
                    };

                    //@todo use PlanetCenter instead of float.zero
                    if (PlanetUtils.GetRandomPointOnSurface(
                                ref CollisionWorld,
                                ref random,
                                casterTransform.Position,
                                float3.zero, // //@todo Planet Center 
                                spellData.BaseCastRange,
                              ref planetFilter,
                                out float3 randomSurfacePos))
                    {
                        targetPosition = randomSurfacePos;
                        targetFound = true;
                    }
                    else
                    {
                        targetPosition = casterTransform.Position;
                        targetFound = true;
                    }

                    targetFound = true;
                    break;
            }

            // Fallback: If "Nearest" targeting fails, aim forward by default
            if (!targetFound && spellData.TargetingMode == ESpellTargetingMode.Nearest)
                targetPosition = casterTransform.Position + (casterTransform.Forward() * spellData.BaseCastRange);


            // --- 4. Spawn Transformation Calculation ---
            float3 spawnPosition = float3.zero;
            quaternion spawnRotation = quaternion.identity;

            bool isProjectile = LinearMovementLookup.HasComponent(spellPrefab);
            bool spawnOnTarget = !isProjectile && !AttachLookup.HasComponent(spellPrefab) && !CopyPositionLookup.HasComponent(spellPrefab);

            float3 fireDirection = float3.zero;

            // Calculate surface alignment for the spawn point
            float3 planetCenter = float3.zero;
            float3 planetSurfaceNormal = math.normalize(targetPosition - planetCenter);

            float3 fallbackForward = math.forward(casterTransform.Rotation);
            PlanetUtils.ProjectDirectionOnSurface(fallbackForward, planetSurfaceNormal, out var planetTangentForward);

            if (spawnOnTarget)
            {
                spawnPosition = targetPosition;
                spawnRotation = TransformLookup.HasComponent(targetEntity) ? TransformLookup[targetEntity].Rotation : quaternion.LookRotationSafe(planetTangentForward, planetSurfaceNormal);
            }
            else
            {
                spawnPosition = casterTransform.Position + (casterTransform.Forward() * spellData.BaseSpawnOffset);

                if (targetFound && isProjectile)
                {
                    float3 vectorToTarget = targetPosition - spawnPosition;
                    float toTargetDistSq = math.lengthsq(vectorToTarget);

                    fireDirection = toTargetDistSq > math.EPSILON ? fireDirection = math.normalize(vectorToTarget) : casterTransform.Forward();
                }
                else
                    fireDirection = casterTransform.Forward();

                spawnRotation = TransformLookup.HasComponent(targetEntity) ? TransformLookup[targetEntity].Rotation : quaternion.identity;
            }


            // --- 5. Entity Instantiation ---
            var spellEntity = ECB.Instantiate(chunkIndex, spellPrefab);

            // --- 6. Component Configuration ---
            
             ECB.SetComponent(chunkIndex, spellEntity, new LocalTransform
            {
                Position = spawnPosition,
                Rotation = spawnRotation,
                Scale = 1f
            });

            // If movement is Follow Target > Spawn on caster and follow target
            if (targetFound && targetEntity != Entity.Null && FollowMovementLookup.HasComponent(spellPrefab) && FollowMovementLookup.IsComponentEnabled(spellPrefab))
            {
                ECB.SetComponent(chunkIndex, spellEntity, new FollowTargetMovement
                {
                    Target = targetEntity,
                    Speed = spellData.BaseSpeed * math.max(1, bonusSpellSpeedMult),
                    StopDistance = 0
                });
            }

            // If movement is Linear > Spawn on caster and go toward set direction
            if (LinearMovementLookup.HasComponent(spellPrefab) && LinearMovementLookup.IsComponentEnabled(spellPrefab))
            {
                ECB.SetComponent(chunkIndex, spellEntity, new LinearMovement
                {
                    Direction = fireDirection,
                    Speed = spellData.BaseSpeed * math.max(1, bonusSpellSpeedMult)
                });
            }

            // Set Orbit Movement if applicable
            if (OrbitMovementLookup.HasComponent(spellPrefab) && OrbitMovementLookup.IsComponentEnabled(spellPrefab))
            {
                float radius = math.length(spellData.BaseSpawnOffset);
                float3 relativeOffset = new float3(0, 0, radius);

                ECB.SetComponent(chunkIndex, spellEntity, new OrbitMovement
                {
                    OrbitCenterEntity = request.Caster,
                    Radius = radius,
                    RelativeOffset = relativeOffset,
                    AngularSpeed = spellData.BaseSpeed * math.max(1, bonusSpellSpeedMult),
                    OrbitCenterPosition = casterTransform.Position
                });
            }

            bool isAttached = AttachLookup.HasComponent(spellPrefab);
            bool isCopingPosition = CopyPositionLookup.HasComponent(spellPrefab);

            // Set Attach to Caster if applicable
            if (isAttached)
            {
                ECB.AddComponent(chunkIndex, spellEntity, new Parent { Value = request.Caster });

                spawnPosition = float3.zero;
                spawnRotation = quaternion.identity;
            }
            // Set CopyPosition if applicable
            else if (isCopingPosition)
            {
                var copyPosition = CopyPositionLookup[spellPrefab];
                copyPosition.Target = request.Caster;

                spawnPosition = casterTransform.Position + (casterTransform.Forward() * spellData.BaseSpawnOffset);

                float3 surfaceNormal = casterTransform.Up();
                float3 forward = casterTransform.Forward();

                PlanetUtils.ProjectDirectionOnSurface(forward, surfaceNormal, out var tangentForward);

                spawnRotation = quaternion.LookRotationSafe(tangentForward, surfaceNormal);

                ECB.SetComponent(chunkIndex, spellEntity, copyPosition);

            }

            if (SelfRotateLookup.HasComponent(spellPrefab))
            {
                ECB.SetComponent(chunkIndex, spellEntity, new SelfRotate
                {
                    RotationSpeed = spellData.BaseSpeed * math.max(1, bonusSpellSpeedMult)
                });
            }

            // Set Damage On Contact
            if (DamageOnContactLookup.HasComponent(spellPrefab))
            {
                ECB.SetComponent(chunkIndex, spellEntity, new DamageOnContact
                {
                    Damage = spellData.BaseDamage + bonusDamage,
                    Element = spellData.Element,
                    AreaRadius = spellData.BaseEffectArea * math.max(1, bonusAreaRadiusMult)
                });
            }

            // Set Damage On Tick 
            if (DamageOnTickLookup.HasComponent(spellPrefab))
            {
                ECB.SetComponent(chunkIndex, spellEntity, new DamageOnTick
                {
                    Caster = request.Caster,
                    TickRate = spellData.TickRate,
                    DamagePerTick = spellData.BaseDamagePerTick + bonusDamage,
                    ElapsedTime = 0f,
                    AreaRadius = spellData.BaseEffectArea * bonusAreaRadiusMult,
                    Element = spellData.Element
                });

                // Scale the visual representation to match the effect area
                ECB.SetComponent<LocalTransform>(chunkIndex, spellEntity, new LocalTransform
                {
                    Position = spawnPosition,
                    Rotation = spawnRotation,
                    Scale = spellData.BaseEffectArea * bonusAreaRadiusMult * 2
                });
            }

            // Set Lifetime
            if (LifetimeLookup.HasComponent(spellPrefab))
            {
                ECB.SetComponent(chunkIndex, spellEntity, new Lifetime
                {
                    Duration = spellData.Lifetime,
                    TimeLeft = spellData.Lifetime
                });
            }

            // Physics Collider
            if (ColliderLookup.HasComponent(spellPrefab))
            {
                var collider = ColliderLookup[spellPrefab];
                collider.Value.Value.SetCollisionFilter(filter);
                ECB.SetComponent(chunkIndex, spellEntity, collider);
            }

            // Set Child Entities Spawner if applicable
            if (ChildSpawnerLookup.HasComponent(spellPrefab))
            {
                if (spellData.ChildPrefabIndex >= 0 && spellData.ChildPrefabIndex < ChildSpellPrefabs.Length)
                {
                    var childPrefabEntity = ChildSpellPrefabs[spellData.ChildPrefabIndex].Prefab;

                    ECB.SetComponent(chunkIndex, spellEntity, new ChildEntitiesSpawner
                    {
                        ChildEntityPrefab = childPrefabEntity,
                        DesiredChildrenCount = spellData.ChildrenCount,
                        CollisionFilter = filter,
                        IsDirty = true
                    });
                }

                // Set Child Circle Layout if applicable
                if (ChildCircleLayoutLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponent(chunkIndex, spellEntity, new ChildEntitiesLayout_Circle
                    {
                        Radius = spellData.ChildrenSpawnRadius,
                        AngleInDegrees = 360
                    });
                }
            }

            // Set Ricochet if applicable
            if (RicochetLookup.HasComponent(spellPrefab))
            {
                int bouncesCount = spellData.Bounces + stats.BouncesAdded;
                ECB.SetComponent(chunkIndex, spellEntity, new Ricochet
                {
                    RemainingBounces = bouncesCount,
                    BounceRange = math.max(1, spellData.BouncesSearchRadius),
                    BounceSpeed = spellData.BaseSpeed * math.max(1, stats.ProjectileSpeedMultiplier),
                });
            }

            // Set Pierce if applicable
            if (PierceLookup.HasComponent(spellPrefab))
            {
                int pierceCount = spellData.Pierces + stats.PierceAdded;
                ECB.SetComponent(chunkIndex, spellEntity, new Pierce
                {
                    RemainingPierces = math.max(1, pierceCount)
                });
            }

            // Cleanup the request entity now that the spell is spawned
            ECB.DestroyEntity(chunkIndex, requestEntity);
        }
    }
}
