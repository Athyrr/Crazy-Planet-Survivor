using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public partial struct CameraFollowSystem : ISystem
{
    
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<CameraSettings>(out var settings))
            return;
        if (!SystemAPI.TryGetSingletonEntity<Player>(out var playerEntity))
            return;
        if (!settings.Camera.IsValid())
            return;
        
        Camera camera = settings.Camera.Value;
        Transform cameraTransform = camera.transform;
        LocalTransform playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;
        float deltaTime = SystemAPI.Time.DeltaTime;
        
        float3 playerPos = playerTransform.Position;
        float3 up = playerTransform.Up();
        
        float3 cameraForward = cameraTransform.forward;
        
        float3 cameraTangentRight = math.normalizesafe(math.cross(cameraForward, up)); 
        float3 nextCameraForward = math.normalizesafe(cameraForward - math.dot(cameraForward, up)*up);
        nextCameraForward = math.rotate(math.normalizesafe(quaternion.AxisAngle(cameraTangentRight, -settings.CameraAngle)), nextCameraForward);
        
        float3 relativePosition = math.rotate(math.normalizesafe(quaternion.AxisAngle(cameraTangentRight, settings.CameraAngle)), up) * settings.CameraDistance;
        float3 targetPosition = playerPos + relativePosition;
        
        float3 camToPlayer = playerPos - (float3)cameraTransform.position;
        quaternion targetRotation = math.normalizesafe(quaternion.LookRotation(math.normalizesafe(camToPlayer), -math.normalizesafe(math.cross(nextCameraForward, cameraTangentRight))));
        
        // Smooth
        float3 smoothedPosition = math.lerp(cameraTransform.position, targetPosition, math.min(deltaTime * settings.Smooth, 1));
        quaternion smoothedRotation = math.slerp(cameraTransform.rotation, targetRotation, math.min(deltaTime * settings.RotationSmooth, 1));
        
        cameraTransform.position = smoothedPosition;
        cameraTransform.rotation = smoothedRotation;

        var cameraData = new CameraWorldData
        {
            Position = targetPosition,
            Forward = math.mul(targetRotation, new float3(0, 0, 1)),
            Right = math.mul(targetRotation, new float3(1, 0, 0)),
            Up = math.mul(targetRotation, new float3(0, 1, 0))
        };

        if (SystemAPI.TryGetSingletonRW<CameraWorldData>(out var cameraWorldData))
        {
            cameraWorldData.ValueRW = cameraData;
        }
        else
        {
            var cameraDataEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(cameraDataEntity, cameraData);
        }



    }
}