using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

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

        UnityEngine.Camera camera = settings.Camera.Value;
        LocalTransform playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;


        float3 worldCameraPos = playerTransform.Position + settings.WorldOffset;
        quaternion worldCameraRot = settings.FixedRotation;

        camera.transform.position = worldCameraPos;
        camera.transform.rotation = worldCameraRot;

        //Entity planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();
        //float3 planetCenter = SystemAPI.GetComponent<LocalTransform>(planetEntity).Position;
        //float3 normal = math.normalize(worldCameraPos - planetCenter);

        var cameraData = new CameraWorldData
        {
            Position = worldCameraPos,
            Forward = math.mul(worldCameraRot, new float3(0, 0, 1) /** normal*/),
            Right = math.mul(worldCameraRot, new float3(1, 0, 0) /** normal*/),
            Up = math.mul(worldCameraRot, new float3(0, 1, 0) /** normal*/)
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