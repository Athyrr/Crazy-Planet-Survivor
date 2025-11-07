using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EntitiesMovementSystem))]
[BurstCompile]
public partial struct AvoidanceSystem : ISystem
{
    private float _lastTickTimer;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;
        
        if (gameState.State != EGameState.Running)
            return;

        if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physicsWorldSingleton))
            return;

        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out Entity planetEntity))
            return;

        //_lastTickTimer -= SystemAPI.Time.DeltaTime;
        //if (_lastTickTimer > 0)
        //    return;
        //_lastTickTimer = 0.1f; // Tick every 0.25 seconds

        var physicsWorld = physicsWorldSingleton.PhysicsWorld;
        var planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
        var playerEntity = SystemAPI.GetSingletonEntity<Player>();
        var playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;

        var avoidanceJob = new AvoidanceJob
        {
            PhysicsWorld = physicsWorld,
            PlanetCenter = planetTransform.Position,
            PlayerPosition = planetTransform.Position
        };
        state.Dependency = avoidanceJob.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// Job that processes avoidance behavior for enemy entities with the Avoidance component.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(Enemy), typeof(Avoidance))]
    private partial struct AvoidanceJob : IJobEntity
    {
        [ReadOnly] public PhysicsWorld PhysicsWorld;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float3 PlayerPosition;

        private const float MaxAvoidanceDistance = 50f * 50f; // Squared distance

        public void Execute(Entity entity, in Avoidance avoidance, in LocalTransform transform, ref SteeringForce steering)
        {
            // Return early if entity is too far from the player
            if (math.distancesq(transform.Position, PlayerPosition) > MaxAvoidanceDistance)
            {
                steering.Value = float3.zero;
                return;
            }

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