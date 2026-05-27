using Unity.Entities;
using UnityEngine;

/// <summary>
/// Adds a Lifetime component to a spell entity for auto-destruction.
/// Spell counterpart of LifetimeAuthoring (which requires DestructibleAuthoring).
/// The Duration is set by SpellCastingSystem from FinalDuration at spawn.
/// </summary>
public class SpellLifetimeAuthoring : MonoBehaviour
{
    class Baker : Baker<SpellLifetimeAuthoring>
    {
        public override void Bake(SpellLifetimeAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Lifetime>(entity);
        }
    }
}
