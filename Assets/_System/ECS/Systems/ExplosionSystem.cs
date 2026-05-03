using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(CollisionSystem))]
[BurstCompile]
public partial struct ExplosionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out GameState gameState) || gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var job = new ProcessExplosionsJob
        {
            ECB = ecb.AsParallelWriter(),
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public partial struct ProcessExplosionsJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;


        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in ExplosionRequest explosion)
        {
            if (explosion.VfxPrefab != Entity.Null)
            {
                var explosionEntity = ECB.Instantiate(chunkIndex, explosion.VfxPrefab);

                ECB.SetComponent(chunkIndex, explosionEntity, new LocalTransform
                {
                    Position = explosion.Position,
                    Rotation = quaternion.identity,
                    Scale = 1f, // Use Explosion Size Stat
                });

                ECB.SetComponent(chunkIndex, explosionEntity, new DamageOnContact()
                {
                    Damage = (int)(explosion.Damage),
                    Tags = explosion.Tags,
                    TargetLayers = explosion.TargetLayers
                });

                // Track damage back to the source spell
                if (explosion.DatabaseIndex != -1)
                {
                    ECB.AddComponent(chunkIndex, explosionEntity, new SpellSource
                    {
                        CasterEntity = explosion.Damager,
                        DatabaseIndex = explosion.DatabaseIndex
                    });
                }

                ECB.AddBuffer<HitEntityMemory>(chunkIndex, explosionEntity);
            }

            ECB.DestroyEntity(chunkIndex, entity);
        }
    }
}

/// <summary>
/// Request for an explosion when a hit happens
/// </summary>
public struct ExplosionRequest : IComponentData
{
    public float3 Position;
    public float Damage;
    public ESpellTag Tags;
    public bool IsCritical;
    public Entity VfxPrefab;

    public uint TargetLayers;
    public int DatabaseIndex;

    public Entity Damager;
    public bool IsPlayer;
}