using Unity.Collections;
using Unity.Transforms;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;

[BurstCompile]
public partial struct AuraDamageSystem : ISystem
{
    private ComponentLookup<Enemy> _enemyLookup;
    private ComponentLookup<LocalTransform> _transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();

        _enemyLookup = state.GetComponentLookup<Enemy>(true);
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
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

        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;

        _enemyLookup.Update(ref state);
        _transformLookup.Update(ref state);

        var auraTickJob = new AuraTickJob
        {
            ECB = ecb.AsParallelWriter(),
            DeltaTime = SystemAPI.Time.DeltaTime,

            PhysicsWorld = physicsWorld,

            EnemyLookup = _enemyLookup,
            TransformLookup = _transformLookup,
        };

        state.Dependency = auraTickJob.ScheduleParallel(state.Dependency);
    }

    private partial struct AuraTickJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        public float DeltaTime;

        [ReadOnly] public PhysicsWorld PhysicsWorld;

        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in DamageOnTick damageOnTick, in LocalTransform transform)
        {


        }
    }
}
