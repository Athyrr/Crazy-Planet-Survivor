using Unity.Collections;
using Unity.Entities;

/// <summary>Enemy prefabs spawnable by spawn_enemy / ttk, baked by SandboxEnemyRegistryAuthoring in SC_Sandbox only.
/// Explicit names (never EntityManager.GetName, unverified in a development build).</summary>
public struct SandboxEnemyPrefab : IBufferElementData
{
    public FixedString64Bytes Name;
    public Entity Prefab;
}

/// <summary>Same entity as the SandboxEnemyPrefab buffer: the dummy prefab instantiated by spawn_dummy.</summary>
public struct SandboxRegistry : IComponentData
{
    public Entity DummyPrefab;
}
