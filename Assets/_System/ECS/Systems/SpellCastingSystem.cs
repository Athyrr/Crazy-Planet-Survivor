using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;

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

        var spellPrefabs = SystemAPI.GetSingletonBuffer<SpellPrefab>(true);
        var spellDatabase = SystemAPI.GetComponent<SpellsDatabase>(spellDatabaseEntity);

        var castJob = new CastSpellJob
        {
            ECB = ecb.AsParallelWriter(),

            SpellDatabaseRef = spellDatabase.Blobs,
            SpellPrefabs = spellPrefabs,

            //@todo optimize lookups via caching in system (lookup.update)

            PlayerLookup = SystemAPI.GetComponentLookup<Player>(true),
            EnemyLookup = SystemAPI.GetComponentLookup<Enemy>(true),

            TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            StatsLookup = SystemAPI.GetComponentLookup<Stats>(true),
            LifetimeLookup = SystemAPI.GetComponentLookup<Lifetime>(true),
            ColliderLookup = SystemAPI.GetComponentLookup<PhysicsCollider>(true),
            AttachLookup = SystemAPI.GetComponentLookup<AttachToCaster>(true),

            DamageOnContactLookup = SystemAPI.GetComponentLookup<DamageOnContact>(true),
            DamageOnTickLookup = SystemAPI.GetComponentLookup<DamageOnTick>(true),

            LinearMovementLookup = SystemAPI.GetComponentLookup<LinearMovement>(true),
            OrbitMovementLookup = SystemAPI.GetComponentLookup<OrbitMovement>(true),
            FollowMovementLookup = SystemAPI.GetComponentLookup<FollowTargetMovement>(true),

            RicochetLookup = SystemAPI.GetComponentLookup<Ricochet>(true),
            PierceLookup = SystemAPI.GetComponentLookup<Pierce>(true)
        };

        state.Dependency = castJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct CastSpellJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public DynamicBuffer<SpellPrefab> SpellPrefabs;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;

        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
        [ReadOnly] public ComponentLookup<Lifetime> LifetimeLookup;
        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;
        [ReadOnly] public ComponentLookup<AttachToCaster> AttachLookup;

        [ReadOnly] public ComponentLookup<DamageOnContact> DamageOnContactLookup;
        [ReadOnly] public ComponentLookup<DamageOnTick> DamageOnTickLookup;

        [ReadOnly] public ComponentLookup<LinearMovement> LinearMovementLookup;
        [ReadOnly] public ComponentLookup<OrbitMovement> OrbitMovementLookup;
        [ReadOnly] public ComponentLookup<FollowTargetMovement> FollowMovementLookup;

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
            var spellPrefab = SpellPrefabs[request.DatabaseIndex].Prefab;

            // Check if prefab exists
            if (spellPrefab == Entity.Null)
            {
                ECB.DestroyEntity(chunkIndex, requestEntity);
                return;
            }

            var caster = request.Caster;
            var casterTransform = TransformLookup[request.Caster];

            // Get caster stats bonus
            //if (StatsLookup.HasComponent(caster))
            //{
            var stats = StatsLookup[caster];
            float bonusDamage = stats.Damage;
            float bonusSpellSpeedMult = stats.ProjectileSpeedMultiplier;
            float bonusAreaRadiusMult = math.max(1, stats.EffectAreaRadiusMult);
            //}

            // Instanciate spell entity
            var spellEntity = ECB.Instantiate(chunkIndex, spellPrefab);

            float3 spawnPosition = float3.zero;
            quaternion spawnRotation = quaternion.identity;

            // Set Attach to Caster if applicable
            if (AttachLookup.HasComponent(spellPrefab))
            {
                ECB.AddComponent(chunkIndex, spellEntity, new Parent { Value = request.Caster });

                spawnPosition = float3.zero;
                spawnRotation = quaternion.identity;
            }
            else
            {
                spawnPosition = casterTransform.Position + casterTransform.Forward() * spellData.BaseSpawnOffset;
                spawnRotation = casterTransform.Rotation;
            }

            // Set initial transform
            ECB.SetComponent(chunkIndex, spellEntity, new LocalTransform
            {
                Position = spawnPosition,
                Rotation = spawnRotation,
                Scale = 1f
            });

            // Set Linear Movement if applicable
            if (LinearMovementLookup.HasComponent(spellPrefab) && LinearMovementLookup.IsComponentEnabled(spellPrefab))
            {
                ECB.SetComponent(chunkIndex, spellEntity, new LinearMovement
                {
                    Direction = casterTransform.Forward(), // @todo handle direction toward target if exists
                    Speed = spellData.BaseSpeed * math.max(1, bonusSpellSpeedMult)
                });
            }

            // Set Orbit Movement if applicable
            else if (OrbitMovementLookup.HasComponent(spellPrefab) && OrbitMovementLookup.IsComponentEnabled(spellPrefab))
            {
                float radius = spellData.BaseSpawnOffset;
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
                if (AttachLookup.HasComponent(spellPrefab))
                {
                    ECB.SetComponent(chunkIndex, spellEntity, new LocalTransform
                    {
                        Position = relativeOffset,
                        Rotation = spawnRotation,
                        Scale = 1f
                    });
                }
            }

            // Set Follow Movement if applicable
            else if (FollowMovementLookup.HasComponent(spellPrefab) && FollowMovementLookup.IsComponentEnabled(spellPrefab))
            {
                ECB.SetComponent(chunkIndex, spellEntity, new FollowTargetMovement
                {
                    Target = request.Target,
                    Speed = spellData.BaseSpeed * math.max(1, bonusSpellSpeedMult),
                    StopDistance = 0f
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
                bool isPlayerCaster = PlayerLookup.HasComponent(caster);
                CollisionFilter filter = new CollisionFilter
                {
                    BelongsTo = isPlayerCaster ? CollisionLayers.PlayerSpell : CollisionLayers.EnemySpell,
                    CollidesWith = (isPlayerCaster ? CollisionLayers.Enemy : CollisionLayers.Player) | CollisionLayers.Obstacle,
                };
                var collider = ColliderLookup[spellPrefab];
                collider.Value.Value.SetCollisionFilter(filter);
                ECB.SetComponent(chunkIndex, spellEntity, collider);
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
