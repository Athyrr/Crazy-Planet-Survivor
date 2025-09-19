using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct CameraFollowSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<CameraSettings>(out var settings))
            return;

        if (!SystemAPI.TryGetSingletonEntity<CameraTarget>(out var playerEntity))
            return;

        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out var planetEntity))
            return;

        if (!settings.Camera.IsValid())
            return;

        var camera = settings.Camera.Value;

        LocalTransform playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;
        PlanetData planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;

        float3 playerPos = playerTransform.Position;
        quaternion playerRot = playerTransform.Rotation;
        float3 planetCenter = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO.Position;
        float3 planetNormal = math.normalize(playerPos - planetCenter);

        // Camera target position
        float3 backward = math.mul(playerRot, new float3(0, 0, -1));
        float3 targetPos = playerPos
                         + backward * settings.Distance
                         + planetNormal * settings.Height;

        Vector3 currentPos = camera.transform.position;
        float3 smoothedPos = math.lerp(currentPos, targetPos, settings.Smooth * SystemAPI.Time.DeltaTime);

        // Target look direction
        float3 lookDirection = math.normalize(playerPos - smoothedPos);
        quaternion targetRotation = quaternion.LookRotationSafe(lookDirection, planetNormal);

        // Smooth rotation
        Quaternion currentRotation = camera.transform.rotation;
        Quaternion smoothedRotation = Quaternion.Slerp(currentRotation, targetRotation, settings.RotationSmooth * SystemAPI.Time.DeltaTime);

        // Apply transforms
        camera.transform.position = smoothedPos;
        camera.transform.rotation = smoothedRotation;


        // Update Camera Data
        var cameraData = new CameraWorldData
        {
            Position = smoothedPos,
            Forward = math.mul(smoothedRotation, new float3(0, 0, 1)),
            Right = math.mul(smoothedRotation, new float3(1, 0, 0))
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
