using Unity.Transforms;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// System that updates the CameraTargetComponent's transform (GameObject) to follow the player's position and rotation set by ECS Movement system.
/// </summary>
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public partial class CameraTargetUpdaterSystem : SystemBase
{
    private Transform _cameraTargetTransform;

    public void OnStartRunning(ref SystemState state)
    {
        if (CameraTargetComponent.Instance != null)
            _cameraTargetTransform = CameraTargetComponent.Instance.transform;
    }

    protected override void OnUpdate()
    {
        if (_cameraTargetTransform == null)
        {
            if (CameraTargetComponent.Instance != null)
                this._cameraTargetTransform = CameraTargetComponent.Instance.transform;
            else
                return;
        }

        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
        LocalTransform playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;

        // Update CameraTarget transform to follow the player
        _cameraTargetTransform.position = playerTransform.Position;
        //_cameraTargetTransform.rotation = playerTransform.Rotation;
        _cameraTargetTransform.up = playerTransform.Up();
    }
}