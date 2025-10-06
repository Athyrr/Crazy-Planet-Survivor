using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
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
        
        float3 arbitrary = new float3(0, 0, 1); // world forward
        if (math.abs(math.dot(up, arbitrary)) > 0.99f)
        {
            arbitrary = new float3(1, 0, 0);
        }
        float3 forward = math.normalizesafe(math.cross(math.cross(arbitrary, up), up));
        
        float3 right = math.cross(up, forward);
        
        float cameraAngle = math.radians(settings.CameraAngle);
        float3 offsetDir = -forward * math.cos(cameraAngle) + up * math.sin(cameraAngle);
        float3 camOffset = offsetDir * settings.CameraDistance;

        float3 targetPosition = playerPos + camOffset;
        float3 toPlayer = math.normalizesafe(playerPos - targetPosition);

        quaternion targetRotation = quaternion.LookRotationSafe(toPlayer, up);
        
        // Smooth
        float3 smoothedPosition = math.lerp(cameraTransform.position, targetPosition, deltaTime * settings.Smooth);
        quaternion smoothedRotation = math.slerp(cameraTransform.rotation, targetRotation, deltaTime * settings.RotationSmooth);

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