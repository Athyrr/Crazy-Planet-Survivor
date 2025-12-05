using Unity.Collections;
using Unity.Transforms;
using Unity.Entities;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(EntitiesMovementSystem))]
[BurstCompile]
public partial struct CopyEntityPositionSystem : ISystem
{
    private ComponentLookup<LocalTransform> _transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CopyEntityPosition>();

        _transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Get game state
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        // Only run when game is running
        if (gameState.State != EGameState.Running)
            return;

        float deltaTime = SystemAPI.Time.DeltaTime;

        _transformLookup.Update(ref state);

        var copyPositionJob = new CopyPositionJob
        {
            DeltaTime = deltaTime,

            TransformLookup = _transformLookup,
        };

        var handle = copyPositionJob.ScheduleParallel(state.Dependency);

        state.Dependency = handle;
    }


    [BurstCompile]
    private partial struct CopyPositionJob : IJobEntity
    {
        public float DeltaTime;

        [NativeDisableContainerSafetyRestriction]
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in CopyEntityPosition copy, ref LocalTransform localTransform)
        {
            if (copy.Target == Entity.Null || !TransformLookup.HasComponent(copy.Target))
                return;

            var copiedEntityTransform = TransformLookup[copy.Target];
            localTransform.Position = copiedEntityTransform.Position + copy.Offset;
        }
    }
}
