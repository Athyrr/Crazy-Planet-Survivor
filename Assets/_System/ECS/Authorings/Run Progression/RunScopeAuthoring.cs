using UnityEngine;
using Unity.Entities;

/// <summary>
/// Tags a prefab's entity as <see cref="RunScope"/> so it is destroyed by RunCleanerSystem when the run ends.
/// </summary>
public class RunScopeAuthoring : MonoBehaviour
{
    private class Baker : Baker<RunScopeAuthoring>
    {
        public override void Bake(RunScopeAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new RunScope());
        }
    }
}
