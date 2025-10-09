using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
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
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var spellDatabaseEntity = SystemAPI.GetSingletonEntity<SpellsDatabase>();
        var spellPrefabs = SystemAPI.GetSingletonBuffer<SpellPrefab>(true);
        var spellDatabase = SystemAPI.GetComponent<SpellsDatabase>(spellDatabaseEntity);

        var fireballJob = new CastFireballJob
        {
            TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            StatsLookup = SystemAPI.GetComponentLookup<Stats>(true),
            ECB = ecb.AsParallelWriter(),
            SpellDatabaseRef = spellDatabase.Blobs,
            SpellPrefabs = spellPrefabs

        };
        state.Dependency = fireballJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(CastSpellRequest), typeof(FireballRequestTag))]
    private partial struct CastFireballJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Stats> StatsLookup;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDatabaseRef;
        [ReadOnly] public DynamicBuffer<SpellPrefab> SpellPrefabs;
        public EntityCommandBuffer.ParallelWriter ECB;

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
                OrbitCenterPosition = casterTransform.Position + casterTransform.Forward() * 50f,
                AngularSpeed = 3,
                Radius = 50,
                RelativeOffset = casterTransform.Forward() * 50f
            };
            var spawnPosition = casterTransform.Position + casterTransform.Forward() * orbitData.Radius;
            ECB.SetComponent(chunkIndex, fireballEntity, new LocalTransform
            {
                Position = spawnPosition,
                Rotation = casterTransform.Rotation,
                Scale = 5f
            });
            ECB.RemoveComponent<LinearMovement>(chunkIndex, fireballEntity);
            ECB.AddComponent(chunkIndex, fireballEntity, orbitData);



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
