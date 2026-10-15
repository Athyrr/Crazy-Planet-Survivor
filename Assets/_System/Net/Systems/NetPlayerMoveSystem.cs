using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Predicted player locomotion on the planet sphere. Runs on the server (authoritative) and on the owning
/// client (prediction + rollback replay). Must be deterministic from (transform, input, tick dt) only.
/// Same terrain-follow approach as the solo EntitiesMovementSystem: raycast towards the planet centre
/// against the Landscape layer, fall back to keeping the current altitude if the ray misses.
/// </summary>
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[UpdateBefore(typeof(PredictedFixedStepSimulationSystemGroup))]
[BurstCompile]
public partial struct NetPlayerMoveSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetPlayer>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new MoveJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            PlanetCenter = SystemAPI.GetSingleton<PlanetData>().Center,
            CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(Simulate))]
    private partial struct MoveJob : IJobEntity
    {
        public float DeltaTime;
        public float3 PlanetCenter;
        [ReadOnly] public CollisionWorld CollisionWorld;

        private const float SNAP_DISTANCE = 50f;

        public void Execute(ref LocalTransform transform, in NetPlayer player, in NetPlayerInput input)
        {
            float throttle = math.saturate(math.length(input.MoveDirection));
            float3 toPlayer = transform.Position - PlanetCenter;
            float altitude = math.length(toPlayer);
            float3 up = toPlayer / altitude;

            PlanetUtils.ProjectDirectionOnSurface(input.MoveDirection, up, out float3 tangent);
            bool moving = throttle >= 0.05f && math.lengthsq(tangent) > 0.001f;

            // Still snapped when idle so a spawn above/below the terrain settles on the ground.
            float3 desired = moving ? transform.Position + tangent * (player.Speed * throttle * DeltaTime) : transform.Position;
            var filter = new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape };

            float3 newUp;
            if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, desired, PlanetCenter, filter, SNAP_DISTANCE, out var hit))
            {
                transform.Position = hit.Position;
                newUp = math.normalize(hit.Position - PlanetCenter);
            }
            else
            {
                PlanetUtils.SnapToSurfaceRadius(desired, PlanetCenter, altitude, out var snapped);
                transform.Position = snapped;
                newUp = math.normalize(snapped - PlanetCenter);
            }

            float3 heading = moving ? tangent : math.mul(transform.Rotation, new float3(0, 0, 1));
            PlanetUtils.ProjectDirectionOnSurface(heading, newUp, out float3 facing);
            if (math.lengthsq(facing) > 0.001f)
                transform.Rotation = quaternion.LookRotationSafe(facing, newUp);
        }
    }
}
