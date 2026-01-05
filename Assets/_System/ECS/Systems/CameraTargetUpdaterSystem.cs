using Unity.Cinemachine;
using Unity.Transforms;
using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Synchronizes a managed Cinemachine camera target with the ECS Player's position.
/// This system handles spherical orientation (Parallel Transport) to ensure the camera 
/// behaves correctly as the player moves around the planet.
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
        // Ensure we have valid references to the managed camera components
        if (!_initialized || _cameraTargetTransform == null)
        {
            InitializeCameraTarget();
            if (!_initialized) return;
        }

        // Retrieve Player position from ECS
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
        LocalTransform playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;

        float3 playerPos = playerTransform.Position;
        float distSq = math.lengthsq(playerPos);
        float dist = math.sqrt(distSq);
        
        // Calculate direction towards the planet center (Gravity/Down direction)
        float3 toCenter = dist > math.EPSILON ? -playerPos / dist : new float3(0, -1, 0);

        // The target transform stays at the planet center (0,0,0) to act as a pivot
        _cameraTargetTransform.position = Vector3.zero; 
        
        // Parallel Transport: Use the current Up vector as a hint to maintain consistent orientation.
        // This prevents the camera from snapping or spinning wildly when passing through the planet's poles.
        Vector3 currentUp = _cameraTargetTransform.up;
        if (math.abs(math.dot(toCenter, (float3)currentUp)) > 0.99f) currentUp = math.rotate(playerTransform.Rotation, new float3(0, 0, 1));

        _cameraTargetTransform.rotation = Quaternion.LookRotation(toCenter, currentUp);

        // Smoothly interpolate the camera radius to prevent jittering caused by rapid height changes (e.g., terrain height maps)
        _cameraTargetFollow.Radius = math.lerp(_cameraTargetFollow.Radius, dist + 35, SystemAPI.Time.DeltaTime * 5f);
    }

    /// <summary>
    /// Locates the CameraTargetComponent in the scene to establish a link between ECS and GameObjects.
    /// </summary>
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