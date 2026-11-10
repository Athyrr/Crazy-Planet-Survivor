using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>Enemy prefabs spawnable by spawn_enemy / ttk and the dummy prefab of spawn_dummy.
/// Lives in SC_Sandbox only, so these prefabs are baked and loaded on the Sandbox planet only.
/// Always compiled: baked type.</summary>
public class SandboxEnemyRegistryAuthoring : MonoBehaviour
{
    [System.Serializable]
    public struct Entry
    {
        [Tooltip("Name typed in spawn_enemy / ttk (exact, then substring). Short: max 61 UTF-8 bytes.")]
        public string Name;
        public GameObject Prefab;
    }

    public Entry[] Enemies = new Entry[0];

    [Tooltip("Prefab instantiated by spawn_dummy.")]
    public GameObject Dummy;

    private class Baker : Baker<SandboxEnemyRegistryAuthoring>
    {
        public override void Bake(SandboxEnemyRegistryAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            var buffer = AddBuffer<SandboxEnemyPrefab>(entity);

            if (authoring.Enemies != null)
            {
                foreach (var entry in authoring.Enemies)
                {
                    if (entry.Prefab == null || string.IsNullOrWhiteSpace(entry.Name))
                    {
                        Debug.LogError($"[SandboxEnemyRegistryAuthoring] entry '{entry.Name}' has no name or prefab, skipped.", authoring);
                        continue;
                    }

                    buffer.Add(new SandboxEnemyPrefab
                    {
                        Name = new FixedString64Bytes(entry.Name),
                        Prefab = GetEntity(entry.Prefab, TransformUsageFlags.Dynamic),
                    });
                }
            }

            AddComponent(entity, new SandboxRegistry
            {
                DummyPrefab = authoring.Dummy != null
                    ? GetEntity(authoring.Dummy, TransformUsageFlags.Dynamic)
                    : Entity.Null,
            });
        }
    }
}
