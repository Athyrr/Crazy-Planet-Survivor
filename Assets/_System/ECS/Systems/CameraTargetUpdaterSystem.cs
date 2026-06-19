using _System.Settings;
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
    private CinemachineHardLookAt _cameraHardLookAt;
    private bool _initialized;

    // Player entity the rig is currently following. The camera rig persists across scene
    // reloads, so a new run / respawn must re-seed the orientation rather than carry over
    // the previous run's drifted 'up' vector.
    private Entity _lastPlayerEntity;

    protected override void OnCreate()
    {
        RequireForUpdate<Player>();
        _initialized = false;
        _lastPlayerEntity = Entity.Null;
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

        // A new run / respawn produces a fresh player entity. Since the camera rig persists,
        // re-seed the orientation this frame instead of inheriting the previous run's drifted up.
        bool playerChanged = playerEntity != _lastPlayerEntity;
        _lastPlayerEntity = playerEntity;

        LocalTransform playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;

        float3 playerPos = playerTransform.Position;
        float distSq = math.lengthsq(playerPos);
        float dist = math.sqrt(distSq);

        // Calculate direction towards the planet center (Gravity/Down direction)
        float3 toCenter = dist > math.EPSILON ? -playerPos / dist : new float3(0, -1, 0);

        // The target transform stays at the planet center (0,0,0) to act as a pivot
        _cameraTargetTransform.position = Vector3.zero;

        Vector3 currentUp = playerChanged ? Vector3.up : _cameraTargetTransform.up;
        if (math.abs(math.dot(toCenter, (float3)currentUp)) > 0.99f) currentUp = math.rotate(playerTransform.Rotation, new float3(0, 0, 1));

        _cameraTargetTransform.rotation = Quaternion.LookRotation(toCenter, currentUp);
        
        float radiusOffset = 35f; // fallback used when no planet data / settings asset is available
        if (CpBaseCameraSettings.I != null && SystemAPI.TryGetSingleton<PlanetData>(out var planetData))
        {
            var cam = CpBaseCameraSettings.PlanetCameraSettings[planetData.PlanetID];

            radiusOffset = cam.RadiusOffset;
            _cameraTargetFollow.VerticalAxis.Value = cam.VerticalAxis;
            _cameraTargetFollow.RadialAxis.Value = cam.RadialAxis;

            if (_cameraHardLookAt != null)
                _cameraHardLookAt.LookAtOffset = new Vector3(0f, dist + cam.LookAtOffsetY, 0f);
        }

        // Smoothly interpolate the camera radius to prevent jittering caused by rapid height changes (e.g., terrain height maps)
        _cameraTargetFollow.Radius = math.lerp(_cameraTargetFollow.Radius, dist + radiusOffset, SystemAPI.Time.DeltaTime * 5f);
    }

    /// <summary>
    /// Locates the CameraTargetComponent in the scene to establish a link between ECS and GameObjects.
    /// </summary>
    private void InitializeCameraTarget()
    {
        var cameraTarget = CameraTargetComponent.Instance != null
            ? CameraTargetComponent.Instance
            : GameObject.FindFirstObjectByType<CameraTargetComponent>();

        if (cameraTarget == null)
            return;

        _cameraTargetTransform = cameraTarget.transform;
        _cameraTargetFollow = cameraTarget.CameraTargetFollow;
        // The aim (HardLookAt) lives on the same CinemachineCamera GameObject as the orbital body.
        _cameraHardLookAt = _cameraTargetFollow != null
            ? _cameraTargetFollow.GetComponent<CinemachineHardLookAt>()
            : null;
        _initialized = true;
    }

    protected override void OnDestroy()
    {
        _cameraTargetTransform = null;
        _initialized = false;
    }
}