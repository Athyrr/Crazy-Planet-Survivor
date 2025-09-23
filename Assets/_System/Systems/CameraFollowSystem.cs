//using Unity.Entities;
//using Unity.Mathematics;
//using Unity.Transforms;
//using UnityEngine;

//[UpdateInGroup(typeof(LateSimulationSystemGroup))]
//public partial struct CameraFollowSystem : ISystem
//{
//    public void OnUpdate(ref SystemState state)
//    {
//        if (!SystemAPI.TryGetSingleton<CameraSettings>(out var settings))
//            return;
//        if (!SystemAPI.TryGetSingletonEntity<Player>(out var playerEntity))
//            return;
//        if (!settings.Camera.IsValid())
//            return;

//        Camera camera = settings.Camera.Value;
//        Transform cameraTransform = camera.transform;
//        LocalTransform playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;
//        float deltaTime = SystemAPI.Time.DeltaTime;

//        float3 playerUp = math.mul(playerTransform.Rotation, new float3(0, 1, 0));

//        quaternion upOnlyRotation = quaternion.LookRotationSafe(
//            math.mul(playerTransform.Rotation, new float3(0, 0, 1)),
//            playerUp
//        );

//        float3 targetPosition = playerTransform.Position + math.mul(upOnlyRotation, settings.LocalOffset);

//        quaternion targetRotation = math.mul(upOnlyRotation, settings.LocalRotation);

//        // Smooth
//        float3 smoothedPosition = math.lerp(cameraTransform.position, targetPosition, deltaTime * settings.Smooth);
//        quaternion smoothedRotation = math.slerp(cameraTransform.rotation, targetRotation, deltaTime * settings.RotationSmooth);

//        cameraTransform.position = smoothedPosition;
//        cameraTransform.rotation = smoothedRotation;

//        var cameraData = new CameraWorldData
//        {
//            Position = targetPosition,
//            Forward = math.mul(targetRotation, new float3(0, 0, 1)),
//            Right = math.mul(targetRotation, new float3(1, 0, 0)),
//            Up = math.mul(targetRotation, new float3(0, 1, 0))
//        };

//        if (SystemAPI.TryGetSingletonRW<CameraWorldData>(out var cameraWorldData))
//        {
//            cameraWorldData.ValueRW = cameraData;
//        }
//        else
//        {
//            var cameraDataEntity = state.EntityManager.CreateEntity();
//            state.EntityManager.AddComponentData(cameraDataEntity, cameraData);
//        }

//    }
//}