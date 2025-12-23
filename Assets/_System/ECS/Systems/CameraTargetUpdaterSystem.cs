using Unity.Cinemachine;
using Unity.Transforms;
using Unity.Entities;
using UnityEngine;


/// <summary>
/// old todo ? mb not available actually
/// @todo look for Camera Targhet component read ecs player transfrom instead of SystemBase (pull method)
/// </summary>
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public partial class CameraTargetUpdaterSystem : SystemBase
{
    private Transform _cameraTargetTransform;
    private CinemachineOrbitalFollow _cameraTargetFollow;
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
                _cameraTargetFollow = CameraTargetComponent.Instance.CameraTargetFollow;
                
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

                // calc fwd & dist
                var forward = Vector3.Normalize(playerTransform.Position);
                var distance = Vector3.Magnitude(playerTransform.Position);

                _cameraTargetTransform.position = Vector3.zero; // actually planet center, to later get planet ref where player are.
                // _cameraTargetTransform.up = forward;
                _cameraTargetTransform.LookAt(playerTransform.Position * -1);
                _cameraTargetFollow.Radius = distance + 35;
            }
        }
    }

    protected override void OnDestroy()
    {
        _cameraTargetTransform = null;
        _initialized = false;
    }
}