using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PhysicsSystemGroup))]
[BurstCompile]
public partial struct RicochetShotSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Stats>();
        state.RequireForUpdate<SpellPrefab>();
        state.RequireForUpdate<ActiveSpell>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<CastSpellRequest>();
        state.RequireForUpdate<RichochetShotRequestTag>();
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


        var ricochetJob = new CastRicochetShotJob
        {
            ECB = ecb.AsParallelWriter(),

            PlayerLookup = SystemAPI.GetComponentLookup<Player>(true),
            TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            StatsLookup = SystemAPI.GetComponentLookup<Stats>(true),
            ColliderLookup = SystemAPI.GetComponentLookup<PhysicsCollider>(true),

            SpellDatabaseRef = spellDatabase.Blobs,
            SpellPrefabs = spellPrefabs
        };
        state.Dependency = ricochetJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(CastSpellRequest), typeof(RichochetShotRequestTag))]
    private partial struct CastRicochetShotJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;

        [ReadOnly] public DynamicBuffer<SpellPrefab> SpellPrefabs;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

        void Execute([ChunkIndexInQuery] int chunkIndex, Entity requestEntity, in CastSpellRequest request)
        {
            if (!SpellDatabaseRef.IsCreated || !TransformLookup.HasComponent(request.Caster))
            {
                ECB.DestroyEntity(chunkIndex, requestEntity);
                return;
            }

            var caster = request.Caster;
            var target = request.Target;

            ref readonly var spellData = ref SpellDatabaseRef.Value.Spells[request.DatabaseIndex];
            var spellPrefab = SpellPrefabs[request.DatabaseIndex].Prefab;

            if (spellPrefab == Entity.Null)
            {
                ECB.DestroyEntity(chunkIndex, requestEntity);
                return;
            }

            var casterTransform = TransformLookup[request.Caster];
            var casterStats = StatsLookup[request.Caster];

            float3 castDirection;
            if (target != Entity.Null && TransformLookup.HasComponent(target))
            {
                var targetTransform = TransformLookup[target];
                castDirection = math.normalize(targetTransform.Position - casterTransform.Position);
            }
            else
            {
                castDirection = casterTransform.Forward();
            }

            //Spell damage calculation
            float damage = spellData.BaseDamage + casterStats.Damage;

            var ricochetEntity = ECB.Instantiate(chunkIndex, spellPrefab);

            ECB.SetComponent(chunkIndex, ricochetEntity, new Projectile
            {
                Damage = damage,
                Element = spellData.Element
            });

            // Linear movement version
            ECB.SetComponent(chunkIndex, ricochetEntity, new LocalTransform
            {
                Position = casterTransform.Position,
                Rotation = casterTransform.Rotation,
                Scale = 1f
            });

            ECB.SetComponent<LinearMovement>(chunkIndex, ricochetEntity, new LinearMovement
            {
                //Direction = castDirection,
                Direction = casterTransform.Forward(),
                Speed = spellData.BaseSpeed
            });


            // Collision 
            bool isPlayerCaster = PlayerLookup.HasComponent(request.Caster);
            CollisionFilter collisionFilter;
            if (isPlayerCaster)
            {
                collisionFilter = new CollisionFilter()
                {
                    BelongsTo = CollisionLayers.PlayerProjectile,
                    CollidesWith = CollisionLayers.Enemy | CollisionLayers.Obstacle,
                };
            }
            else
            {
                collisionFilter = new CollisionFilter()
                {
                    BelongsTo = CollisionLayers.EnemyProjectile,
                    CollidesWith = CollisionLayers.Player | CollisionLayers.Obstacle,
                };
            }
            PhysicsCollider collider = ColliderLookup[spellPrefab];
            collider.Value.Value.SetCollisionFilter(collisionFilter);
            ECB.SetComponent(chunkIndex, ricochetEntity, collider);

            // Set Ricochet component
            int bouncesCount = spellData.Bounces + casterStats.BouncesAdded;
            ECB.AddComponent(chunkIndex, ricochetEntity, new RicochetData
            {
                RemainingBounces = bouncesCount,
                SearchRadius = math.max(1, spellData.BouncesSearchRadius),
                Speed = spellData.BaseSpeed * math.max(1, casterStats.ProjectileSpeedMultiplier),
            });
            //ECB.SetComponent(chunkIndex, ricochetEntity, new RicochetData
            //{
            //    RemainingBounces = bouncesCount,
            //    SearchRadius = spellData.BouncesSearchRadius,
            //    Speed = spellData.BaseSpeed * casterStats.ProjectileSpeedMultiplier,
            //});

            // Lifetime
            ECB.SetComponent(chunkIndex, ricochetEntity, new Lifetime
            {
                ElapsedTime = spellData.Lifetime,
                Duration = spellData.Lifetime
            });

            // Destroy request
            ECB.DestroyEntity(chunkIndex, requestEntity);
        }
    }
}
