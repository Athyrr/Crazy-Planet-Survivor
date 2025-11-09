using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections; // Pour ToArchetypeChunkArray

public class DebugGizmoDrawer : MonoBehaviour
{
    [Header("Debugs")]
    public bool DrawSteeringVectors;
    public bool DrawAvoidanceRadius;
    public bool DrawPlayerCollectRange;

    [Header("Colors")]
    public Color SteeringColor = Color.red;
    public Color AvoidanceRadiusColor = new Color(1f, 0.5f, 0f);
    public Color CollectRangeColor = Color.cyan;

    private EntityManager _entityManager;
    private EntityQuery _enemyQuery;
    private EntityQuery _playerQuery;

    ComponentTypeHandle<LocalTransform> _transformTypeHandle;
    ComponentTypeHandle<SteeringForce> _steeringTypeHandle;
    ComponentTypeHandle<Avoidance> _avoidanceTypeHandle;


    void Start()
    {
        if (World.DefaultGameObjectInjectionWorld == null)
        {
            this.enabled = false;
            return;
        }
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _enemyQuery = _entityManager.CreateEntityQuery(
            typeof(Enemy),
            typeof(LocalTransform),
            typeof(Avoidance),
            typeof(SteeringForce)
        );

        _playerQuery = _entityManager.CreateEntityQuery(
            typeof(Player),
            typeof(LocalTransform),
            typeof(Stats)
        );

        _transformTypeHandle = _entityManager.GetComponentTypeHandle<LocalTransform>(true);
        _steeringTypeHandle = _entityManager.GetComponentTypeHandle<SteeringForce>(true);
        _avoidanceTypeHandle = _entityManager.GetComponentTypeHandle<Avoidance>(true);
    }

    void OnDrawGizmos()
    {
        if (_entityManager == null)
            return;
        // Draw Steering Vectors
        if (DrawSteeringVectors && !_enemyQuery.IsEmpty)
        {
            Gizmos.color = SteeringColor;
            using var chunks = _enemyQuery.ToArchetypeChunkArray(Allocator.Temp);
            foreach (var chunk in chunks)
            {
                var transforms = chunk.GetNativeArray(ref _transformTypeHandle);
                var steerings = chunk.GetNativeArray(ref _steeringTypeHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var transform = (LocalTransform)transforms[i];
                    var steering = (SteeringForce)steerings[i];

                    if (math.lengthsq(steering.Value) > 0.001f)
                    {
                        Gizmos.DrawRay(transform.Position, steering.Value);
                    }
                }
            }
        }

        // Draw Avoidance Radii
        if (DrawAvoidanceRadius && !_enemyQuery.IsEmpty)
        {
            Gizmos.color = AvoidanceRadiusColor;
            using var chunks = _enemyQuery.ToArchetypeChunkArray(Allocator.Temp);
            foreach (var chunk in chunks)
            {
                var transforms = chunk.GetNativeArray(ref _transformTypeHandle);
                var avoidances = chunk.GetNativeArray(ref _avoidanceTypeHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var transform = (LocalTransform)transforms[i];
                    var avoidance = (Avoidance)avoidances[i];

                    DrawWireDisk(transform.Position, transform.Up(), avoidance.Radius);
                }
            }
        }

        // Draw Player Collect Range
        if (DrawPlayerCollectRange && !_playerQuery.IsEmpty)
        {
            Gizmos.color = CollectRangeColor;
            var stats = _playerQuery.GetSingleton<Stats>();
            var transform = _playerQuery.GetSingleton<LocalTransform>();

            DrawWireDisk(transform.Position, transform.Up(), stats.CollectRange);
        }
    }

    private static void DrawWireDisk(float3 position, float3 normal, float radius)
    {
        quaternion rotation = quaternion.LookRotation(normal, math.up());

        if (math.all(normal == math.up()))
        {
            rotation = quaternion.identity;
        }
        else
        {
            rotation = quaternion.LookRotation(math.forward(), normal);
        }

        Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
        Gizmos.matrix = matrix;

        int segments = 32;
        float angle = 0f;
        float3 lastPos = new float3(math.cos(angle) * radius, 0, math.sin(angle) * radius);
        for (int i = 1; i <= segments; i++)
        {
            angle = (i / (float)segments) * 2 * math.PI;
            float3 newPos = new float3(math.cos(angle) * radius, 0, math.sin(angle) * radius);
            Gizmos.DrawLine(lastPos, newPos);
            lastPos = newPos;
        }

        Gizmos.matrix = Matrix4x4.identity;
    }
}