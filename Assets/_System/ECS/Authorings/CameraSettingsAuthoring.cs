using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;

public class CameraSettingsAuthoring : MonoBehaviour
{
    [Header("Refs")]
    public Camera Camera;
    public GameObject PlayerTarget;

    private float CameraDistance;
    private quaternion CameraUpToOffset;
    [Range(1f, 30f)] public float Smooth = 8f;
    [Range(1f, 30f)] public float RotationSmooth = 10f;

    private void OnValidate()
    {
        float3 cameraOffset = gameObject.transform.position - PlayerTarget.transform.position;
        cameraOffset = math.rotate(math.inverse(PlayerTarget.transform.rotation), cameraOffset);
        float3 up = new float3(0, 1, 0);
        
        CameraDistance = math.length(cameraOffset);

        if (math.dot(math.normalizesafe(cameraOffset), up) > 0.999)
        {
            CameraUpToOffset = quaternion.identity;
            return;
        } 
        if (math.dot(math.normalizesafe(cameraOffset), up) < -0.999)
        {
            CameraUpToOffset = math.inverse(quaternion.identity);
            return;
        }
        
        CameraUpToOffset.value.xyz = math.cross(up, cameraOffset);
        CameraUpToOffset.value.w = math.sqrt(math.square(CameraDistance) * math.square(math.length(up))) + math.dot(up, cameraOffset);

        CameraUpToOffset = math.normalizesafe(CameraUpToOffset);
    }

    class Baker : Baker<CameraSettingsAuthoring>
    {
        public override void Bake(CameraSettingsAuthoring authoring)
        {
            if (authoring.Camera == null || authoring.PlayerTarget == null) return;

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            Transform cameraTransform = authoring.Camera.transform;
            Transform playerTransform = authoring.PlayerTarget.transform;

            float3 worldOffset = cameraTransform.position - playerTransform.position;
            quaternion inversePlayerRotation = math.inverse(playerTransform.rotation);
            float3 localOffset = math.mul(inversePlayerRotation, worldOffset);

            quaternion localRotation = math.mul(inversePlayerRotation, cameraTransform.rotation);

            AddComponent(entity, new CameraSettings
            {
                Camera = authoring.Camera,
                Smooth = authoring.Smooth,
                RotationSmooth = authoring.RotationSmooth,
                CameraDistance = authoring.CameraDistance,
                CameraUpToOffset = authoring.CameraUpToOffset,
                LocalOffset = localOffset,
                LocalRotation = localRotation
            });
        }
    }
}