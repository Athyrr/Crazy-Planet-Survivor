using Unity.Transforms;
using Unity.Entities;
using UnityEngine;


/// <summary>
/// @todo look for Camera Targhet component read ecs player transfrom instead of SystemBase (pull method)
/// </summary>
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public partial class CameraTargetUpdaterSystem : SystemBase
{
    private Transform _cameraTargetTransform;
    private bool _initialized;

    protected override void OnCreate()
    {
        RequireForUpdate<Player>();
        _initialized = false;
    }

    protected override void OnUpdate()
    {
        if (!_initialized || _cameraTargetTransform == null)
        {
            if (CameraTargetComponent.Instance != null)
            {
                _cameraTargetTransform = CameraTargetComponent.Instance.transform;
                _initialized = true;
            }
            else
            {
                Debug.LogWarning("CameraTargetComponent instance not found. Searching for camera target...");
                var cameraTarget = GameObject.FindFirstObjectByType<CameraTargetComponent>();
                if (cameraTarget != null)
                {
                    _cameraTargetTransform = cameraTarget.transform;
                    _initialized = true;
                }
                else
                {
                    Debug.LogError("No CameraTargetComponent found in scene!");
                    return;
                }
            }
        }

        if (SystemAPI.TryGetSingletonEntity<Player>(out Entity playerEntity))
        {
            if (SystemAPI.HasComponent<LocalTransform>(playerEntity))
            {
                LocalTransform playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;

                _cameraTargetTransform.position = playerTransform.Position;
                _cameraTargetTransform.up = playerTransform.Up();
            }
        }
    }

    protected override void OnDestroy()
    {
        _cameraTargetTransform = null;
        _initialized = false;
    }
}