using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
internal partial struct RotatingBladeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Stats>();
        state.RequireForUpdate<SpellPrefab>();
        state.RequireForUpdate<ActiveSpell>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<CastSpellRequest>();
        state.RequireForUpdate<RotatingBladeRequestTag>();
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

        var bladeQuery = SystemAPI.QueryBuilder()
            .WithAll<RotatingBladeIndex>()
            .Build();

        int existingBladeCount = bladeQuery.CalculateEntityCount();

        var RotatingBladeJob = new CastRotatingBladeJob
        {
            ECB = ecb.AsParallelWriter(),
            ExistingBladeCount = existingBladeCount,

            PlayerLookup = SystemAPI.GetComponentLookup<Player>(true),
            TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            StatsLookup = SystemAPI.GetComponentLookup<Stats>(true),
            ColliderLookup = SystemAPI.GetComponentLookup<PhysicsCollider>(true),

            SpellDatabaseRef = spellDatabase.Blobs,
            SpellPrefabs = spellPrefabs
        };
        state.Dependency = RotatingBladeJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(CastSpellRequest), typeof(RotatingBladeRequestTag))]
    private partial struct CastRotatingBladeJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public int ExistingBladeCount;


        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
        [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;

        [ReadOnly] public DynamicBuffer<SpellPrefab> SpellPrefabs;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;

        private void Execute([ChunkIndexInQuery] int chunkIndex, Entity requestEntity, in CastSpellRequest request)
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

            // if (spellData.InstanciateOnce)
            //     return;


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
            var damage = spellData.BaseDamage + casterStats.Damage;

            var rotatingBladeEntity = ECB.Instantiate(chunkIndex, spellPrefab);

            ECB.SetComponent(chunkIndex, rotatingBladeEntity, new Projectile
            {
                Damage = damage,
                Element = spellData.Element
            });

            // Orbit movement version
            // Compter le nombre total de blades actives pour ce caster

            int bladeIndex = ExistingBladeCount + chunkIndex;
            int totalBlades = ExistingBladeCount + 1; // Estimation

            float angleOffset = totalBlades > 0 ? (2f * math.PI * bladeIndex) / totalBlades : 0f;

            var orbitData = new OrbitMovement
            {
                OrbitCenterEntity = caster,
                OrbitCenterPosition = casterTransform.Position + casterTransform.Forward() * 5,
                AngularSpeed = 4,
                Radius = 5,
                RelativeOffset = casterTransform.Forward() * 5,
                //InitialAngle = angleOffset
            };

            // // Position initiale basée sur l'angle
            var offset = new float3(
                math.cos(angleOffset) * orbitData.Radius,
                0,
                math.sin(angleOffset) * orbitData.Radius
            );
            var spawnPosition = casterTransform.Position + offset;
            ECB.SetComponent(chunkIndex, rotatingBladeEntity, new LocalTransform
            {
                Position = spawnPosition,
                Rotation = casterTransform.Rotation,
                Scale = 0.7f
            });
            ECB.RemoveComponent<LinearMovement>(chunkIndex, rotatingBladeEntity);
            ECB.AddComponent(chunkIndex, rotatingBladeEntity, orbitData);

            ECB.AddComponent(chunkIndex, rotatingBladeEntity, new RotatingBladeIndex
            {
                Index = bladeIndex,
                TotalBlades = totalBlades
            });

            // Linear movement version
            /*ECB.SetComponent(chunkIndex, fireballEntity, new LocalTransform
        {
            Position = casterTransform.Position,
            Rotation = casterTransform.Rotation,
            Scale = 1f
        });

        ECB.SetComponent<LinearMovement>(chunkIndex, fireballEntity, new LinearMovement
        {
            //Direction = castDirection,
            Direction = casterTransform.Forward(),
            Speed = spellData.BaseSpeed
        });*/

            // Collision
            var isPlayerCaster = PlayerLookup.HasComponent(request.Caster);
            CollisionFilter collisionFilter;
            if (isPlayerCaster)
                collisionFilter = new CollisionFilter
                {
                    BelongsTo = CollisionLayers.PlayerProjectile,
                    CollidesWith = CollisionLayers.Enemy | CollisionLayers.Obstacle
                };
            else
                collisionFilter = new CollisionFilter
                {
                    BelongsTo = CollisionLayers.EnemyProjectile,
                    CollidesWith = CollisionLayers.Player | CollisionLayers.Obstacle
                };
            var collider = ColliderLookup[spellPrefab];
            collider.Value.Value.SetCollisionFilter(collisionFilter);
            ECB.SetComponent(chunkIndex, rotatingBladeEntity, collider);

            if (spellData.IsInvincible)
                ECB.AddComponent<Invincible>(chunkIndex, rotatingBladeEntity);

            ECB.SetComponent(chunkIndex, rotatingBladeEntity, new Lifetime
            {
                ElapsedTime = spellData.Lifetime,
                Duration = spellData.Lifetime
            });

            // Destroy request
            ECB.DestroyEntity(chunkIndex, requestEntity);
        }
    }
}