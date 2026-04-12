using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Rotates entities around their local up axis while maintaining alignment with the planet's surface.
/// This is typically used for items, pick-ups, or environmental objects that need a "spinning" animation.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct SelfRotateSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // Ensure the system only runs if there are entities with the SelfRotate component
        state.RequireForUpdate<SelfRotate>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Only process rotation while the game is actively running
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out var planetEntity))
            return;

        var planetCenter = SystemAPI.GetComponent<PlanetData>(planetEntity).Center;

        // Schedule the parallel rotation job
        var selfRotateJob = new SelfRotateJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            PlanetCenter = planetCenter,
        };

        var handle = selfRotateJob.ScheduleParallel(state.Dependency);

        state.Dependency = handle;
    }

    /// <summary>
    /// Calculates the new rotation for an entity, ensuring it stays upright relative to the planet center.
    /// </summary>
    [BurstCompile]
    private partial struct SelfRotateJob : IJobEntity
    {
        [ReadOnly] public float3 PlanetCenter;

        /// <summary> Time elapsed since the last frame. </summary>
        [ReadOnly] public float DeltaTime;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in SelfRotate selfRotate,
            ref LocalTransform localTransform)
        {
            // Calculate the surface normal (Up direction) at the entity's current position
            float3 surfaceNormal = math.normalize(localTransform.Position - PlanetCenter);

            // Project the current forward vector onto the tangent plane to maintain consistent orientation
            float3 currentForward = localTransform.Forward();
            float3 tangentForward =
                math.normalize(currentForward - math.dot(currentForward, surfaceNormal) * surfaceNormal);

            // Create a base rotation that aligns the entity's 'Up' with the planet's surface normal
            quaternion alignedRotation = quaternion.LookRotationSafe(tangentForward, surfaceNormal);

            // Calculate the incremental rotation around the local Y-axis
            float angle = math.radians(selfRotate.RotationSpeed) * DeltaTime;
            localTransform.Rotation = math.mul(alignedRotation, quaternion.RotateY(angle));
        }
    }
}