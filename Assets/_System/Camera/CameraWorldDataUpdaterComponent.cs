using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Updates the ECS camera world data component data based on the Camera postion.
/// </summary>
/// <remarks>This component retrieves the camera's position, forward, up, and right vectors each frame and updates
/// the corresponding <see cref="CameraWorldData"/> component in the ECS world.</remarks>
[DefaultExecutionOrder(100)]
public class CameraWorldDataUpdaterComponent : MonoBehaviour
{
    private Camera _camera;

    private EntityManager _entityManager;
    private EntityQuery _cameraWorldDataQuery;
    private Entity _cameraDataEntity = Entity.Null;

    void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        _entityManager = world.EntityManager;

        var query = _entityManager.CreateEntityQuery(typeof(CameraWorldData));
        if (query.IsEmpty)
            _cameraDataEntity = _entityManager.CreateEntity(typeof(CameraWorldData));
        else
            _cameraDataEntity = query.GetSingletonEntity();

        var entityManager = world.EntityManager;
        _cameraWorldDataQuery = entityManager.CreateEntityQuery(typeof(CameraWorldData));
    }


    private void LateUpdate()
    {
        if (_cameraWorldDataQuery.IsEmpty)
            return;

        _entityManager.SetComponentData(_cameraDataEntity, new CameraWorldData
        {
            Position = _camera.transform.position,
            Forward = _camera.transform.forward,
            Up = _camera.transform.up,
            Right = _camera.transform.right
        });
    }
}