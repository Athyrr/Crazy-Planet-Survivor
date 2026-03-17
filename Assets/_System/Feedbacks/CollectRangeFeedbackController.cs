using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class CollectRangeFeedbackController : MonoBehaviour
{
    private EntityManager _entityManager;

    private EntityQuery _playerStatsQuery;
    private EntityQuery _playerTransformQuery;

    private Material _materialInstance;
    private int _radiusRatioPropertyID;

    private void Start()
    {
        if (World.DefaultGameObjectInjectionWorld == null)
        {
            Debug.LogError("ECS World not found!");
            this.enabled = false;
            return;
        }

        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _playerStatsQuery = _entityManager.CreateEntityQuery(typeof(Player), typeof(CoreStats));
        _playerTransformQuery = _entityManager.CreateEntityQuery(typeof(Player), typeof(LocalTransform));

        var renderer = GetComponent<Renderer>();
        _materialInstance = renderer.material;
        _radiusRatioPropertyID = Shader.PropertyToID("_RadiusRatio");

        if (!_materialInstance.HasProperty(_radiusRatioPropertyID))
        {
            Debug.LogError($"Shader does not have property 'RadiusRatio'", this);
            this.enabled = false;
        }
    }

    private void Update()
    {
        RefreshVisual();
    }

    private void LateUpdate()
    {
        RefreshPosition();
    }

    public void RefreshVisual()
    {
    }

    public void RefreshPosition()
    {
        if (_playerStatsQuery.IsEmpty || _materialInstance == null)
            return;

        var playerEntity = _playerTransformQuery.GetSingletonEntity();
        var playerTransform = _entityManager.GetComponentData<LocalTransform>(playerEntity);

        // Position
        transform.position = playerTransform.Position;

        // Rotation
        float3 worldForward = math.forward();
        var playerUp = playerTransform.Up();
        float3 projectedForward = worldForward - math.dot(worldForward, playerUp) * playerUp;
        projectedForward = math.normalize(projectedForward);

        quaternion rotation = quaternion.LookRotationSafe(projectedForward, playerUp);
        transform.rotation = math.mul(rotation, quaternion.Euler(math.radians(90f), 0f, 0f));
    }

    private void OnDestroy()
    {
        if (_materialInstance != null)
        {
            Destroy(_materialInstance);
        }
    }
}