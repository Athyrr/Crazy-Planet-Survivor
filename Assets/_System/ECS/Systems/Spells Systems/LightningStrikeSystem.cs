//using Unity.Burst;
//using Unity.Physics;
//using Unity.Entities;
//using Unity.Transforms;
//using Unity.Collections;
//using Unity.Mathematics;

//[BurstCompile]
//[UpdateInGroup(typeof(SimulationSystemGroup))]
//public partial struct LightningStrikeSystem : ISystem
//{
//    [BurstCompile]
//    public void OnCreate(ref SystemState state)
//    {
//        state.RequireForUpdate<Stats>();
//        state.RequireForUpdate<SpellPrefab>();
//        state.RequireForUpdate<ActiveSpell>();
//        state.RequireForUpdate<CastSpellRequest>();
//        state.RequireForUpdate<LightningStrikeRequestTag>();
//    }

//    [BurstCompile]
//    public void OnUpdate(ref SystemState state)
//    {
//        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
//            return;

//        if (gameState.State != EGameState.Running)
//            return;

//        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
//        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

//        var spellDatabaseEntity = SystemAPI.GetSingletonEntity<SpellsDatabase>();
//        var spellPrefabs = SystemAPI.GetSingletonBuffer<SpellPrefab>(true);
//        var spellDatabase = SystemAPI.GetComponent<SpellsDatabase>(spellDatabaseEntity);

//        var job = new CastSpellJob
//        {
//            ECB = ecb.AsParallelWriter(),

//            PlayerLookup = SystemAPI.GetComponentLookup<Player>(true),
//            TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
//            StatsLookup = SystemAPI.GetComponentLookup<Stats>(true),
//            ColliderLookup = SystemAPI.GetComponentLookup<PhysicsCollider>(true),

//            SpellDatabaseRef = spellDatabase.Blobs,
//            SpellPrefabs = spellPrefabs
//        };
//        state.Dependency = job.ScheduleParallel(state.Dependency);
//    }

//    [BurstCompile]
//    [WithAll(typeof(CastSpellRequest), typeof(LightningStrikeRequestTag))]
//    private partial struct CastSpellJob : IJobEntity
//    {
//        public EntityCommandBuffer.ParallelWriter ECB;

//        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
//        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
//        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
//        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;

//        [ReadOnly] public DynamicBuffer<SpellPrefab> SpellPrefabs;
//        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

//        private void Execute([ChunkIndexInQuery] int chunkIndex, Entity requestEntity, in CastSpellRequest request)
//        {
//            if (!SpellDatabaseRef.IsCreated || !TransformLookup.HasComponent(request.Caster))
//            {
//                ECB.DestroyEntity(chunkIndex, requestEntity);
//                return;
//            }

//            var caster = request.Caster;
//            var target = request.Target;

//            //var spellData = request.GetSpellData();
//            ref readonly var spellData = ref SpellDatabaseRef.Value.Spells[request.DatabaseIndex];
//            var spellPrefab = SpellPrefabs[request.DatabaseIndex].Prefab;


//            if (spellPrefab == Entity.Null)
//            {
//                ECB.DestroyEntity(chunkIndex, requestEntity);
//                return;
//            }

//            var casterTransform = TransformLookup[request.Caster];
//            var casterStats = StatsLookup[request.Caster];
//            //var targetTransform = TransformLookup[request.Target];

//            //float3 castDirection;
//            //if (target != Entity.Null && TransformLookup.HasComponent(target))
//            //    castDirection = math.normalize(TransformLookup[target].Position - casterTransform.Position);
//            //else
//            //    castDirection = casterTransform.Forward();

//            // Spell damage calculation
//            float damage = spellData.BaseDamage + casterStats.Damage;

//            var projectileEntity = ECB.Instantiate(chunkIndex, spellPrefab);

//            ECB.SetComponent(chunkIndex, projectileEntity, new DamageOnContact()
//            {
//                Damage = damage,
//                Element = spellData.Element
//            });

//            ECB.SetComponent(chunkIndex, projectileEntity, new LocalTransform
//            {
//                Position = casterTransform.Position,
//                Rotation = casterTransform.Rotation,
//                Scale = 1f
//            });

//            bool isPlayerCaster = PlayerLookup.HasComponent(request.Caster);
//            CollisionFilter collisionFilter;
//            if (isPlayerCaster)
//            {
//                collisionFilter = new CollisionFilter()
//                {
//                    BelongsTo = CollisionLayers.PlayerProjectile,
//                    CollidesWith = CollisionLayers.Enemy | CollisionLayers.Obstacle,
//                };
//            }
//            else
//            {
//                collisionFilter = new CollisionFilter()
//                {
//                    BelongsTo = CollisionLayers.EnemyProjectile,
//                    CollidesWith = CollisionLayers.Player | CollisionLayers.Obstacle,
//                };
//            }
//            PhysicsCollider collider = ColliderLookup[spellPrefab];
//            collider.Value.Value.SetCollisionFilter(collisionFilter);
//            ECB.SetComponent(chunkIndex, projectileEntity, collider);


//            ECB.SetComponent<LinearMovement>(chunkIndex, projectileEntity, new LinearMovement
//            {
//                Direction = casterTransform.Forward(),
//                Speed = spellData.BaseSpeed
//            });


//            ECB.AddComponent(chunkIndex, projectileEntity, new Lifetime
//            {
//                TimeLeft = spellData.Lifetime,
//                Duration = spellData.Lifetime
//            });

//            // Destroy request entity after spell instancing
//            ECB.DestroyEntity(chunkIndex, requestEntity);
//        }
//    }
//}
