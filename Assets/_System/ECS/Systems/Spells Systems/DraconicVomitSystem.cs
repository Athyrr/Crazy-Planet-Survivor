/*
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

partial struct DraconicVomitSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Stats>();
        state.RequireForUpdate<SpellPrefab>();
        state.RequireForUpdate<ActiveSpell>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<CastSpellRequest>();
        state.RequireForUpdate<DraconicVomitRequestTag>();
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

            RandomSeed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1,

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
    [WithAll(typeof(CastSpellRequest), typeof(DraconicVomitRequestTag))]
    private partial struct CastFireballJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public uint RandomSeed;

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

            var draconicVomitEntity = ECB.Instantiate(chunkIndex, spellPrefab);

            ECB.SetComponent(chunkIndex, draconicVomitEntity, new Projectile
            {
                Damage = damage,
                Element = spellData.Element
            });

            // Orbit movement version
            //var orbitData = new OrbitMovement
            //{
            //    OrbitCenterEntity = caster,
            //    OrbitCenterPosition = casterTransform.Position + casterTransform.Forward() * 5, // @todo change distance by (2) by value  /!\ value same as radius
            //    AngularSpeed = 4,
            //    Radius = 5,
            //    RelativeOffset = casterTransform.Forward() * 5
            //};
            //var spawnPosition = casterTransform.Position + casterTransform.Forward() * orbitData.Radius;
            //ECB.SetComponent(chunkIndex, fireballEntity, new LocalTransform
            //{
            //    Position = spawnPosition,
            //    Rotation = casterTransform.Rotation,
            //    Scale = 0.7f
            //});
            //ECB.RemoveComponent<LinearMovement>(chunkIndex, fireballEntity);
            //ECB.AddComponent(chunkIndex, fireballEntity, orbitData);

            var random = Random.CreateFromIndex(RandomSeed);

            // Linear movement version
            ECB.SetComponent(chunkIndex, draconicVomitEntity, new LocalTransform
            {
                Position = new float3(
                    x: casterTransform.Position.x + random.NextInt(-10,10),
                    y: casterTransform.Position.y + random.NextInt(-10,10),
                    z: casterTransform.Position.z + random.NextInt(-10,10)),
                Rotation = casterTransform.Rotation,
                Scale = 1f
            });

            ECB.SetComponent<LinearMovement>(chunkIndex, draconicVomitEntity, new LinearMovement
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
            ECB.SetComponent(chunkIndex, draconicVomitEntity, collider);

            ECB.SetComponent(chunkIndex, draconicVomitEntity, new Lifetime
            {
                ElapsedTime = spellData.Lifetime,
                Duration = spellData.Lifetime
            });

            // Destroy request
            ECB.DestroyEntity(chunkIndex, requestEntity);
        }
    }
}
*/
