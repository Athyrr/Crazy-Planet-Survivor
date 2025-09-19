using Unity.Entities;
using UnityEngine;

public class CameraSettingsAuthoring : MonoBehaviour
{
    [Header("Camera Reference")]
    public Camera Camera;

    [Header("Follow Settings")]

    [Range(1f, 30f)]
    public float Smooth = 8f;

    [Range(1f, 10f)]
    public float RotationSmooth = 5f;

    [Range(2f, 50f)]
    public float Distance = 5f;

    [Range(0f, 50f)]
    public float Height = 2f;

    class Baker : Baker<CameraSettingsAuthoring>
    {
        public override void Bake(CameraSettingsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new CameraSettings
            {
                Camera = authoring.Camera,
                Smooth = authoring.Smooth,
                RotationSmooth = authoring.RotationSmooth,
                Distance = authoring.Distance,
                Height = authoring.Height
            });
        }
    }
}
