using Unity.Cinemachine;
using Unity.Transforms;
using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;


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
        // Optimization: Handle initialization cleanly
        if (!_initialized || _cameraTargetTransform == null)
        {
            InitializeCameraTarget();
            if (!_initialized) return;
        }





        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
        LocalTransform playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;


        float3 playerPos = playerTransform.Position;
        float distSq = math.lengthsq(playerPos);
        float dist = math.sqrt(distSq);
        float3 toCenter = dist > math.EPSILON ? -playerPos / dist : new float3(0, -1, 0);

        _cameraTargetTransform.position = Vector3.zero; // actually planet center, to later get planet ref where player are.
        
        // Use current Up as hint to maintain orientation (Parallel Transport) to avoid pole singularity
        Vector3 currentUp = _cameraTargetTransform.up;
        if (math.abs(math.dot(toCenter, (float3)currentUp)) > 0.99f) currentUp = math.rotate(playerTransform.Rotation, new float3(0, 0, 1));

        _cameraTargetTransform.rotation = Quaternion.LookRotation(toCenter, currentUp);

        // Smooth the radius change to avoid camera jumping due to height map (pour niels le type qu'aime pas quand ca rebondi)
        _cameraTargetFollow.Radius = math.lerp(_cameraTargetFollow.Radius, dist + 35, SystemAPI.Time.DeltaTime * 5f);
    }

    private void InitializeCameraTarget()
    {
        if (CameraTargetComponent.Instance != null)
        {
            _cameraTargetTransform = CameraTargetComponent.Instance.transform;
            _cameraTargetFollow = CameraTargetComponent.Instance.CameraTargetFollow;
            _initialized = true;
        }
        else
        {
            var cameraTarget = GameObject.FindFirstObjectByType<CameraTargetComponent>();
            if (cameraTarget != null)
            {
                _cameraTargetTransform = cameraTarget.transform;
                _cameraTargetFollow = cameraTarget.CameraTargetFollow;
                _initialized = true;
            }
        }
    }

    protected override void OnDestroy()
    {
        _cameraTargetTransform = null;
        _initialized = false;
    }
}