using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;

public class CameraSettingsAuthoring : MonoBehaviour
{
    [Header("Refs")]
    public Camera Camera;
    public GameObject PlayerTarget;

    [Header("Settings")]
    [Min(1)] public float CameraDistance;
    [Range(0f, 90f)] public float CameraAngle;
    [Range(1f, 30f)] public float Smooth = 8f;
    [Range(1f, 30f)] public float RotationSmooth = 10f;

    class Baker : Baker<CameraSettingsAuthoring>
    {
        public override void Bake(CameraSettingsAuthoring authoring)
        {
            if (authoring.Camera == null || authoring.PlayerTarget == null) 
                return;

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
                CameraAngle = authoring.CameraAngle,
                CameraDistance = authoring.CameraDistance,
                LocalOffset = localOffset,
                LocalRotation = localRotation
            });
        }
    }
}