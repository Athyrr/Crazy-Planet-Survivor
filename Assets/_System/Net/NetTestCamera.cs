using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Net test camera: follows the locally owned player ghost (GhostOwnerIsLocal) in the client world, top-down over
/// the planet surface. The screen "up" direction is parallel-transported along the sphere so the view never spins
/// when crossing a pole (same idea as the solo CameraTargetUpdaterSystem, without Cinemachine).
/// </summary>
public class NetTestCamera : MonoBehaviour
{
    [SerializeField] private float _height = 28f;
    [SerializeField] private float _back = 14f;
    [SerializeField] private float _smoothing = 8f;

    private World _world;
    private EntityQuery _localPlayerQuery;
    private EntityQuery _planetQuery;
    private float3 _screenUp = new float3(0, 0, 1);
    private bool _hasTarget;

    private void LateUpdate()
    {
        var world = ClientServerBootstrap.ClientWorld;
        if (world == null || !world.IsCreated)
        {
            _world = null;
            return;
        }

        if (_world != world)
        {
            _world = world;
            _localPlayerQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<GhostOwnerIsLocal>(), ComponentType.ReadOnly<LocalToWorld>());
            _planetQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlanetData>());
            _hasTarget = false;
        }

        if (_localPlayerQuery.IsEmpty || _planetQuery.IsEmpty)
            return;

        float3 planetCenter = _planetQuery.GetSingleton<PlanetData>().Center;
        // LocalToWorld (not LocalTransform): includes the predicted/interpolated smoothing netcode applies for rendering.
        float3 playerPos = _localPlayerQuery.GetSingleton<LocalToWorld>().Position;
        float3 up = math.normalize(playerPos - planetCenter);

        // Parallel transport: keep the previous screen-up, re-projected on the new tangent plane.
        float3 projected = _screenUp - math.dot(_screenUp, up) * up;
        if (math.lengthsq(projected) < 1e-4f)
            projected = math.cross(up, new float3(1, 0, 0));
        _screenUp = math.normalize(projected);

        float3 desiredPos = playerPos + up * _height - _screenUp * _back;
        var desiredRot = Quaternion.LookRotation(playerPos - desiredPos, _screenUp);

        if (!_hasTarget)
        {
            transform.SetPositionAndRotation(desiredPos, desiredRot);
            _hasTarget = true;
            return;
        }

        float t = 1f - math.exp(-_smoothing * Time.deltaTime);
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, desiredPos, t),
            Quaternion.Slerp(transform.rotation, desiredRot, t));
    }
}
