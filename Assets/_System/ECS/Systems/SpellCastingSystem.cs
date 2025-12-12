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
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
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
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var spellDatabaseEntity = SystemAPI.GetSingletonEntity<SpellsDatabase>();

        var mainSpellPrefabs = SystemAPI.GetSingletonBuffer<SpellPrefab>(true);
        var childSpellPrefabs = SystemAPI.GetSingletonBuffer<ChildSpellPrefab>(true);
        var spellDatabase = SystemAPI.GetComponent<SpellsDatabase>(spellDatabaseEntity);

        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        var castJob = new CastSpellJob
        {
            ECB = ecb.AsParallelWriter(),
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1,

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
            PierceLookup = SystemAPI.GetComponentLookup<Pierce>(true)
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
            // Check if caster bdd & caster exists
            if (!SpellDatabaseRef.IsCreated || !TransformLookup.HasComponent(request.Caster))
            {
                ECB.DestroyEntity(chunkIndex, requestEntity);
                return;
            }

            // Get spell datas
            ref readonly var spellData = ref SpellDatabaseRef.Value.Spells[request.DatabaseIndex];
            var spellPrefab = MainSpellPrefabs[request.DatabaseIndex].Prefab;

            // Check if prefab  or child prefab exists 
            if (spellPrefab == Entity.Null && spellData.ChildPrefabIndex == -1) // -1 : default value for none
            {
                ECB.DestroyEntity(chunkIndex, requestEntity);
                return;
            }

            var caster = request.Caster;
            var casterTransform = TransformLookup[caster];
            bool isPlayerCaster = PlayerLookup.HasComponent(caster);

            // Collision filter
            var filter = new CollisionFilter
            {
                BelongsTo = isPlayerCaster ? CollisionLayers.PlayerSpell : CollisionLayers.EnemySpell,
                CollidesWith = (isPlayerCaster ? CollisionLayers.Enemy : CollisionLayers.Player) | CollisionLayers.Obstacle,
            };

            Entity targetEntity = Entity.Null;
            float3 targetPosition = float3.zero; // Spawn postion or aim position 
            bool targetFound = false;

            var random = Random.CreateFromIndex(Seed);

            // Get caster stats bonus
            //if (StatsLookup.HasComponent(caster))
            //{
            var stats = StatsLookup[caster];
            float bonusDamage = stats.Damage;
            float bonusSpellSpeedMult = stats.ProjectileSpeedMultiplier;
            float bonusAreaRadiusMult = math.max(1, stats.EffectAreaRadiusMult);
            //}

            // Set targetPosition 
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

            // Fallabck CastForward if not target found
            if (!targetFound && spellData.TargetingMode == ESpellTargetingMode.Nearest)
                targetPosition = casterTransform.Position + (casterTransform.Forward() * spellData.BaseCastRange);



            //Set spawn position/rotation
            float3 spawnPosition = float3.zero;
            quaternion spawnRotation = quaternion.identity;

            bool isProjectile = LinearMovementLookup.HasComponent(spellPrefab);
            bool spawnOnTarget = !isProjectile && !AttachLookup.HasComponent(spellPrefab) && !CopyPositionLookup.HasComponent(spellPrefab);

            float3 fireDirection = float3.zero;

            float3 planetCenter = float3.zero;
            float3 planetSurfaceNormal = math.normalize(targetPosition - planetCenter);

            float3 fallbackForward = math.forward(casterTransform.Rotation);
            PlanetUtils.ProjectDirectionOnSurface(fallbackForward, planetSurfaceNormal, out var planetTangentForward);

            if (spawnOnTarget)
            {
                spawnPosition = targetPosition;
                //spawnRotation = quaternion.identity;
                spawnRotation = TransformLookup.HasComponent(targetEntity) ? TransformLookup[targetEntity].Rotation : quaternion.LookRotationSafe(planetTangentForward, planetSurfaceNormal);
            }
            else
            {
                spawnPosition = casterTransform.Position + (casterTransform.Forward() * spellData.BaseSpawnOffset);

                if (targetFound && isProjectile)
                {
                    //var dir = math.normalize(targetPosition - spawnPosition);
                    //fireDirection =  (dir == float3.zero) ? casterTransform.Forward() : dir;

                    float3 vectorToTarget = targetPosition - spawnPosition;
                    float toTargetDistSq = math.lengthsq(vectorToTarget);

                    // normalize if dir > 0, 
                    fireDirection = toTargetDistSq > math.EPSILON ? fireDirection = math.normalize(vectorToTarget) : casterTransform.Forward();
                }
                else
                    //direction = casterTransform.Rotation;
                    fireDirection = casterTransform.Forward();

                //spawnRotation = quaternion.LookRotationSafe(fireDirection, math.up());
                spawnRotation = TransformLookup.HasComponent(targetEntity) ? TransformLookup[targetEntity].Rotation : quaternion.identity;
            }


            // Instanciate spell entity
            var spellEntity = ECB.Instantiate(chunkIndex, spellPrefab);

            // Set components

            // Set  transform
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
                    //Direction = math.forward(spawnRotation),
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

                // If AttachToCaster, set relative offset
                //if (isAttached)
                //{
                //    ECB.SetComponent(chunkIndex, spellEntity, new LocalTransform
                //    {
                //        Position = relativeOffset,
                //        Rotation = spawnRotation,
                //        Scale = 1f
                //    });
                //}
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

                //float3 tangentForward = math.normalize(forward - math.dot(forward, surfaceNormal) * surfaceNormal);
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

                // @todo REMOVE AND USE SHADER
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

            ECB.DestroyEntity(chunkIndex, requestEntity);
        }
    }
}
