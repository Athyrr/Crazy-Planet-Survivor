using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct LightningStrikeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Stats>();
        state.RequireForUpdate<ActiveSpell>();
        state.RequireForUpdate<CastSpellRequest>();
        state.RequireForUpdate<LightningStrikeRequestTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var spellDatabaseEntity = SystemAPI.GetSingletonEntity<SpellsDatabase>();
        var spellPrefabs = SystemAPI.GetSingletonBuffer<SpellPrefab>(true);
        var spellDatabase = SystemAPI.GetComponent<SpellsDatabase>(spellDatabaseEntity);

        var job = new CastSpellJob
        {
            TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
            StatsLookup = SystemAPI.GetComponentLookup<Stats>(true),
            ECB = ecb.AsParallelWriter(),
            SpellDatabaseRef = spellDatabase.Blobs,
            SpellPrefabs = spellPrefabs
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(CastSpellRequest), typeof(LightningStrikeRequestTag))]
    private partial struct CastSpellJob : IJobEntity
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

            //var spellData = request.GetSpellData();
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

            // Spell damage calculation
            float damage = spellData.BaseDamage + casterStats.Damage;

            var fireballEntity = ECB.Instantiate(chunkIndex, spellPrefab);
            ECB.SetComponent(chunkIndex, fireballEntity, new LocalTransform
            {
                Position = casterTransform.Position,
                Rotation = casterTransform.Rotation,
                Scale = 5f
            });

            ECB.SetComponent<LinearMovement>(chunkIndex, fireballEntity, new LinearMovement
            {
                Direction = casterTransform.Forward(),
                Speed = spellData.BaseSpeed
            });

            ECB.DestroyEntity(chunkIndex, requestEntity);
        }
    }
}
