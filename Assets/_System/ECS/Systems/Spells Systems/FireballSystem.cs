using Unity.Burst;
using Unity.Physics;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct FireballSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Stats>();
        state.RequireForUpdate<SpellPrefab>();
        state.RequireForUpdate<ActiveSpell>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<CastSpellRequest>();
        state.RequireForUpdate<FireballRequestTag>();
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

        var fireballJob = new CastFireballJob
        {
            ECB = ecb.AsParallelWriter(),
            
            PlayerLookup = SystemAPI.GetComponentLookup<Player>(true),
            TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            StatsLookup = SystemAPI.GetComponentLookup<Stats>(true),
            ColliderLookup = SystemAPI.GetComponentLookup<PhysicsCollider>(true),
          
            SpellDatabaseRef = spellDatabase.Blobs,
            SpellPrefabs = spellPrefabs

        };
        state.Dependency = fireballJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(CastSpellRequest), typeof(FireballRequestTag))]
    private partial struct CastFireballJob : IJobEntity
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
            //var targetTransform = TransformLookup[request.Target];

            //float3 castDirection;
            //if (target != Entity.Null && TransformLookup.HasComponent(target))
            //    castDirection = math.normalize(TransformLookup[target].Position - casterTransform.Position);
            //else
            //    castDirection = casterTransform.Forward();

            //Spell damage calculation
            float damage = spellData.BaseDamage + casterStats.Damage;

            var fireballEntity = ECB.Instantiate(chunkIndex, spellPrefab);

            ECB.SetComponent(chunkIndex, fireballEntity, new Projectile
            {
                Damage = damage,
                Element = spellData.Element
            });

            // Orbit movement version
            var orbitData = new OrbitMovement
            {
                OrbitCenterEntity = caster,
                OrbitCenterPosition = casterTransform.Position + casterTransform.Forward() * 5, // @todo change distance by (2) by value  /!\ value same as radius
                AngularSpeed = 4,
                Radius = 5,
                RelativeOffset = casterTransform.Forward() * 5
            };
            var spawnPosition = casterTransform.Position + casterTransform.Forward() * orbitData.Radius;
            ECB.SetComponent(chunkIndex, fireballEntity, new LocalTransform
            {
                Position = spawnPosition,
                Rotation = casterTransform.Rotation,
                Scale = 0.7f
            });
            ECB.RemoveComponent<LinearMovement>(chunkIndex, fireballEntity);
            ECB.AddComponent(chunkIndex, fireballEntity, orbitData);

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
            ECB.SetComponent(chunkIndex, fireballEntity, collider);

            // Linear movement version
            //ECB.SetComponent(chunkIndex, fireballEntity, new LocalTransform
            //{
            //    Position = casterTransform.Position,
            //    Rotation = casterTransform.Rotation,
            //    Scale = 100f
            //});

            //ECB.SetComponent<LinearMovement>(chunkIndex, fireballEntity, new LinearMovement
            //{
            //    Direction = casterTransform.Forward(),
            //    Speed = spellData.BaseSpeed
            //});


            ECB.SetComponent(chunkIndex, fireballEntity, new Lifetime
            {
                ElapsedTime = spellData.Lifetime,
                Duration = spellData.Lifetime
            });

            // Destroy request
            ECB.DestroyEntity(chunkIndex, requestEntity);
        }
    }
}
