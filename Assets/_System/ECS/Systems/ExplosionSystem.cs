using _System.ECS.Components.Entity;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Authoring;
using Unity.Transforms;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(CollisionSystem))]
[BurstCompile]
public partial struct ExplosionSystem : ISystem
{
    private ComponentLookup<PhysicsCollider> _colliderLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        //state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();

        _colliderLookup = state.GetComponentLookup<PhysicsCollider>(true);
    }


    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out GameState gameState) || gameState.State != EGameState.Running)
            return;

        //var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecbSingleton = SystemAPI.GetSingleton<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        _colliderLookup.Update(ref state);

        var job = new ProcessExplosionsJob
        {
            ECB = ecb.AsParallelWriter(),
            ColliderLookup = _colliderLookup
        };

        //todo c'est pt jsp pk faut playback mais le playback dit qu'il faut pas

        //state.Dependency = job.ScheduleParallel(state.Dependency);
        //var dep = job.Schedule(state.Dependency);


        //state.Dependency = dep;

        //ecb.Playback(state.EntityManager);
    }

    [BurstCompile]
    public partial struct ProcessExplosionsJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly]
        public ComponentLookup<PhysicsCollider> ColliderLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in ExplosionRequest explosion)
        {
            if (explosion.VfxPrefab != Entity.Null)
            {
                var explosionEntity = ECB.Instantiate(chunkIndex, explosion.VfxPrefab);

                var filter = new CollisionFilter
                {
                    //todo maybe need a belong to (if so pass the entire filter inside the request)
                    CollidesWith = explosion.TargetLayers
                };





                //if (ColliderLookup.HasComponent(explosionEntity))
                //{
                    var col = ColliderLookup[explosionEntity];
                    col.Value.Value.SetCollisionFilter(filter);
                    //ColliderLookup[explosionEntity] = col;
                    ECB.SetComponent(chunkIndex, explosionEntity, col);
                //}



                // ECB.SetComponent(chunkIndex, explosionTrigger, new CollisionFilter(
                // {
                //     CollidesWith = explosion.TargetLayers
                // });

                
                ECB.SetComponent(chunkIndex, explosionEntity, new LocalTransform
                {
                    Position = explosion.Position,
                    Rotation = quaternion.identity,
                    Scale = 1f,
                });

                // Configure the trigger for damage
                ECB.SetComponent(chunkIndex, explosionEntity, new DamageOnContact()
                {
                    Damage = (int)(explosion.Damage),
                    Tag = explosion.Element,
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
    public ESpellTag Element;
    public bool IsCritical;
    public Entity VfxPrefab;

    public uint TargetLayers;
    public int DatabaseIndex;

    public Entity Damager;
    public bool IsPlayer;
}