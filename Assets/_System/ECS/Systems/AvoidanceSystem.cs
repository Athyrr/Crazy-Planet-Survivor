using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EntitiesMovementSystem))]
[BurstCompile]
public partial struct AvoidanceSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var gameState = SystemAPI.GetSingleton<GameState>();
        if (gameState.State != EGameState.Running)
            return;

        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out Entity planetEntity))
            return;

        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
        var planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;

        var avoidanceJob = new AvoidanceJob
        {
            PhysicsWorld = physicsWorld,
            PlanetCenter = planetTransform.Position
        };
        state.Dependency = avoidanceJob.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// Job that processes avoidance behavior for enemy entities with the Avoidance component.
    /// </summary>
    //[BurstCompile]
    [WithAll(typeof(Enemy), typeof(Avoidance))]
    private partial struct AvoidanceJob : IJobEntity
    {
        [ReadOnly] public PhysicsWorld PhysicsWorld;
        [ReadOnly] public float3 PlanetCenter;

        public void Execute(Entity entity, in Avoidance avoidance, in LocalTransform transform, ref SteeringForce steering)
        {
            // Temporary list to store Overlap sphere hits
            var hits = new NativeList<DistanceHit>(Allocator.TempJob);

            // Avoidance force accumulator
            float3 avoidanceForce = float3.zero;

            // Cast an overlap sphere to detect nearby enemies
            PhysicsWorld.OverlapSphere(
                transform.Position,
                avoidance.Radius,
                ref hits,
                new CollisionFilter
                {
                    BelongsTo = CollisionLayers.Raycast,
                    CollidesWith = CollisionLayers.Enemy,
                });

            // Calculate avoidance force based on detected hits
            foreach (var hit in hits)
            {
                if (hit.Entity == entity)
                    continue;

                if (hit.Entity == Entity.Null)
                    continue;

                // Distance between the hit entity and the current entity
                float distance = hit.Distance;

                // Direction vector from the hit point to the current entity 
                //float3 direction = math.normalize(hit.SurfaceNormal);
                float3 direction = hit.SurfaceNormal;

                if (distance > 0.01f)
                {
                    avoidanceForce += (1 / distance) * direction;
                }
            }

            PlanetMovementUtils.GetSurfaceNormalRadius(transform.Position, PlanetCenter, out float3 surfaceNormal);
            PlanetMovementUtils.ProjectDirectionOnSurface(avoidanceForce, surfaceNormal, out avoidanceForce);

            steering.Value = avoidanceForce * avoidance.Weight;

            // Free the tempo list
            hits.Dispose();
        }
    }
}