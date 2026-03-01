using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Unity.VisualScripting;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(CollisionSystem))]
[BurstCompile]
public partial struct ExplosionSystem : ISystem
{
    private ComponentLookup<Enemy> _enemyLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();

        _enemyLookup = state.GetComponentLookup<Enemy>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out GameState gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        _enemyLookup.Update(ref state);

        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var job = new ProcessExplosionsJob
        {
            ECB = ecb.AsParallelWriter(),
            CollisionWorld = collisionWorld,
            EnemyLookup = _enemyLookup
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public partial struct ProcessExplosionsJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in ExplosionRequest explosion)
        {
            if (explosion.VfxPrefab != Entity.Null)
            {
                var vfx = ECB.Instantiate(chunkIndex, explosion.VfxPrefab);
                ECB.SetComponent(chunkIndex, vfx, new LocalTransform
                {
                    Position = explosion.Position,
                    Scale = explosion.Radius
                });
            }

            var hits = new NativeList<DistanceHit>(Allocator.Temp);
            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = explosion.TargetLayers
            };

            if (CollisionWorld.OverlapSphere(explosion.Position, explosion.Radius, ref hits, filter))
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    var hitEntity = hits[i].Entity;

                    // todo Hande case where obstacles dont have damage bufffer + health

                    if (hitEntity == Entity.Null)
                        continue;

                    ECB.AppendToBuffer(chunkIndex, hitEntity, new DamageBufferElement
                    {
                        Damage = explosion.Damage,
                        Element = explosion.Element,
                        IsCritical = false //todo add crit for explosions 
                    });

                    // todo apply Knockback if needed
                    // Explosion Request must store if knockback or not
                }
            }

            hits.Dispose();

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
    public float Radius;
    public float Damage;
    public ESpellTag Element;
    public float CritIntensity;
    public Entity VfxPrefab;

    public uint TargetLayers;
    //public float KnockbackForce;
}