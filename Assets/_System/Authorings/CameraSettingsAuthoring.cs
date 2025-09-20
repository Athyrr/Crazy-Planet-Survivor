using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;


public class CameraSettingsAuthoring : MonoBehaviour
{
    [Header("Camera Reference")]
    public Camera Camera;

    [Header("Target Reference")]
    [Tooltip("The player GameObject to calculate offset from")]
    public GameObject PlayerTarget;

    [Header("Offset Calculation")]
    [Tooltip("Auto-calculate offset from current camera position relative to player")]
    public bool AutoCalculateOffset = true;

    [Header("Manual Override")]
    public Vector3 ManualWorldOffset = new Vector3(0, 2, -5);
    public Vector3 ManualRotationEuler = new Vector3(15, 0, 0);

    [Header("Legacy Settings (unused with offset)")]
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
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            float3 worldOffset;
            quaternion  fixedRotation; ;

            if (authoring.AutoCalculateOffset && authoring.PlayerTarget != null)
            {
                Vector3 cameraPos = authoring.Camera.transform.position;
                Vector3 playerPos = authoring.PlayerTarget.transform.position;
                Quaternion cameraRot = authoring.Camera.transform.rotation;
                Quaternion playerRot = authoring.PlayerTarget.transform.rotation;

                 worldOffset = cameraPos - playerPos;
                fixedRotation = authoring.Camera.transform.rotation;
            }
            else
            {
                worldOffset = authoring.ManualWorldOffset;
                fixedRotation = Quaternion.Euler(authoring.ManualRotationEuler);
            }

            AddComponent(entity, new CameraSettings
            {
                Camera = authoring.Camera,
                Smooth = authoring.Smooth,
                RotationSmooth = authoring.RotationSmooth,
                Distance = authoring.Distance,
                Height = authoring.Height,
                WorldOffset = worldOffset,
                FixedRotation = fixedRotation
            });
        }
    }
}