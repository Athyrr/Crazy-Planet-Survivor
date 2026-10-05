using Unity.Entities;
using UnityEngine;

/// <summary>
/// Common base of the capability slot authorings (Bounce, Pierce). A slot is baked DISABLED: the spell cast
/// (SpellCastingSystem) turns it on when the final counter is > 0 AND the SpellSO allows it.
/// </summary>
[RequireComponent(typeof(DestructibleAuthoring))]
public abstract class SpellCapabilityAuthoring : MonoBehaviour
{
    /// <summary>Adds T and bakes it disabled.</summary>
    public static void BakeDisabled<T>(IBaker baker, Entity entity)
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        baker.AddComponent<T>(entity);
        baker.SetComponentEnabled<T>(entity, false);
    }
}
