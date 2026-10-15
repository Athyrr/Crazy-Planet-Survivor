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
            if (throttle < 0.05f)
                return;

            float3 toPlayer = transform.Position - PlanetCenter;
            float altitude = math.length(toPlayer);
            float3 up = toPlayer / altitude;

            PlanetUtils.ProjectDirectionOnSurface(input.MoveDirection, up, out float3 tangent);
            if (math.lengthsq(tangent) < 0.001f)
                return;

            float3 desired = transform.Position + tangent * (player.Speed * throttle * DeltaTime);
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

            PlanetUtils.ProjectDirectionOnSurface(tangent, newUp, out float3 facing);
            transform.Rotation = quaternion.LookRotationSafe(facing, newUp);
        }
    }
}
