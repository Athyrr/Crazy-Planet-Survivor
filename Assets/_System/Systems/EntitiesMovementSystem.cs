using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct EntitiesMovementSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlanetData>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out Entity planetEntity))
            return;

        //EndSimulationEntityCommandBufferSystem.Singleton ecbSystem = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        //EntityCommandBuffer.ParallelWriter ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        float delta = SystemAPI.Time.DeltaTime;
        LocalTransform planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
        PlanetData planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;

        // Entity Query for Parralel job required NativeArrays
        EntityQuery linearQuery = SystemAPI.QueryBuilder()
          .WithAll<LocalTransform, LinearMovement>().Build();

        if (!linearQuery.IsEmpty)
        {
            // Retrieve components array from the entity query
            var transforms = linearQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            var movements = linearQuery.ToComponentDataArray<LinearMovement>(Allocator.TempJob);

            var linearJob = new MoveLinearJob
            {
                transforms = transforms,
                movements = movements,
                deltaTime = delta,
                PlanetCenter = planetTransform.Position,
                PlanetRadius = planetData.Radius
            };

            // Dispatch in threads
            var linearHandle = linearJob.Schedule(transforms.Length, 64);

            // Ensure finish job
            linearHandle.Complete();

            // Apply results back 
            linearQuery.CopyFromComponentDataArray(transforms);

            // Free memory (on est en cpp ou quoi)
            transforms.Dispose(linearHandle);
            movements.Dispose(linearHandle);
        }

        EntityQuery followQuery = SystemAPI.QueryBuilder().WithAll<LocalTransform, FollowTargetMovement>().Build();

        if (!followQuery.IsEmpty && SystemAPI.TryGetSingletonEntity<Player>(out Entity playerEntity))
        {
            // Player position
            var playerPos = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;

            // Retrieve components array from the entity query
            var followTransforms = followQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            var followMovements = followQuery.ToComponentDataArray<FollowTargetMovement>(Allocator.TempJob);

            var followJob = new MoveFollowJob
            {
                transforms = followTransforms,
                movements = followMovements,
                playerPosition = playerPos,
                deltaTime = delta,
                PlanetCenter = planetTransform.Position,
                PlanetRadius = planetData.Radius
            };

            // Dispatch in threads
            var followHandle = followJob.Schedule(followTransforms.Length, 128);

            // Ensure finish job
            followHandle.Complete();

            // Apply result
            followQuery.CopyFromComponentDataArray<LocalTransform>(followTransforms);

            followTransforms.Dispose(followHandle);
            followMovements.Dispose(followHandle);
        }

        OrbitMovementJob orbitMovementJob = new OrbitMovementJob
        {
            DeltaTime = delta,
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius
        };
        orbitMovementJob.ScheduleParallel();
    }


    [BurstCompile]
    private struct MoveLinearJob : IJobParallelFor
    {
        public float3 PlanetCenter;
        public float PlanetRadius;

        public NativeArray<LocalTransform> transforms;
        public NativeArray<LinearMovement> movements;
        [ReadOnly] public float deltaTime;

        public void Execute(int index)
        {
            var transform = transforms[index];
            LinearMovement movement = movements[index];

            float3 position = transform.Position;
            float3 direction = movement.Direction;
            float speed = movement.Speed;

            if (math.lengthsq(direction) < 0.001f)
                return;


            // Get normal at entity position
            float3 normal = math.normalize(position - PlanetCenter);

            // Project direction on surface
            float3 tangentDirection = direction - math.dot(direction, normal) * normal;
            tangentDirection = math.normalize(tangentDirection);

            // Calculate target projected position
            float3 targetPosition = position + tangentDirection * speed * deltaTime;

            // Snap to surface
            float3 snappedPosition = PlanetCenter + math.normalize(targetPosition - PlanetCenter) * PlanetRadius;

            // Rotation
            quaternion rotation = quaternion.LookRotationSafe(tangentDirection, normal);

            // Apply new direction
            movement.Direction = tangentDirection;

            // Apply new transform
            transform = new LocalTransform
            {
                Position = snappedPosition,
                Rotation = rotation,
                Scale = transform.Scale
            };

            transforms[index] = transform;
        }
    }


    [BurstCompile]
    private struct MoveFollowJob : IJobParallelFor
    {
        public NativeArray<LocalTransform> transforms;
        [ReadOnly] public NativeArray<FollowTargetMovement> movements;
        [ReadOnly] public float3 playerPosition;
        [ReadOnly] public float deltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        public void Execute(int index)
        {
            var transform = transforms[index];
            var movement = movements[index];

            float3 position = transform.Position;

            // Direction vers le player
            float3 directionToPlayer = math.normalize(playerPosition - position);

            // Get normal at entity position
            float3 normal = math.normalize(position - PlanetCenter);

            // Project direction on surface
            float3 tangentDirection = directionToPlayer - math.dot(directionToPlayer, normal) * normal;
            tangentDirection = math.normalize(tangentDirection);

            // Calculate target projected position
            float3 targetPosition = position + tangentDirection * movement.Speed * deltaTime;

            // Snap to surface
            float3 snappedPosition = PlanetCenter + math.normalize(targetPosition - PlanetCenter) * PlanetRadius;

            // Rotation
            quaternion rotation = quaternion.LookRotationSafe(tangentDirection, normal);

            // Apply new transform
            transforms[index] = new LocalTransform
            {
                Position = snappedPosition,
                Rotation = rotation,
                Scale = transform.Scale
            };
        }
    }

    [BurstCompile]
    partial struct OrbitMovementJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 PlanetCenter;
        [ReadOnly] public float PlanetRadius;

        void Execute(ref LocalTransform transform, ref OrbitMovement movement)
        {
            float3 orbitCenter = movement.OrbitCenter;

            float3 orbitVector = transform.Position - orbitCenter;

            float3 orbitNormal = math.normalize(orbitCenter - PlanetCenter);
            quaternion rotation = quaternion.AxisAngle(orbitNormal, movement.AngularSpeed * DeltaTime);

            float3 rotatedVector = math.mul(rotation, orbitVector);
            float3 newOrbitPosition = orbitCenter + rotatedVector;

            float3 newPosition = PlanetCenter + math.normalize(newOrbitPosition - PlanetCenter) * PlanetRadius;

            float3 normal = math.normalize(newPosition - PlanetCenter);
            float3 tangentDirection = math.normalize(math.cross(normal, orbitNormal));

            transform.Position = newPosition;
            transform.Rotation = quaternion.LookRotationSafe(tangentDirection, normal);
        }
    }

}

