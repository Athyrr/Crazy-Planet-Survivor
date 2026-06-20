using UnityEngine;
using Unity.Entities;

/// <summary>
/// Bakes a default <see cref="SpellSource"/> onto a spell prefab so SpellCastingSystem can fill its
/// values with SetComponent instead of AddComponent — avoiding a structural change (archetype shift)
/// per cast and keeping ECB Instantiate batching intact. Tag-only: TransformUsageFlags.None so it
/// composes with the prefab's other authorings without imposing a transform requirement.
/// </summary>
public class SpellSourceAuthoring : MonoBehaviour
{
    private class Baker : Baker<SpellSourceAuthoring>
    {
        public override void Bake(SpellSourceAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new SpellSource { CasterEntity = Entity.Null, DatabaseIndex = 0 });
        }
    }
}
