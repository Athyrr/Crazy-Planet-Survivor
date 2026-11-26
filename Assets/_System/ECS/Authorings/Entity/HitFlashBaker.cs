using Unity.Entities;
using UnityEngine;

/// <summary>
/// Adds HitFlash (disabled) to the entity of any MeshRenderer that belongs to a destructible (enemy or player:
/// both carry DestructibleAuthoring) and whose material declares _HitFlash. The flash lives on the renderer entity,
/// not on the root. Summons have no DestructibleAuthoring and get no flash.
/// </summary>
public class HitFlashBaker : Baker<MeshRenderer>
{
    private const string HitFlashProperty = "_HitFlash";

    public override void Bake(MeshRenderer authoring)
    {
        if (GetComponentInParent<DestructibleAuthoring>() == null)
            return;

        bool declaresFlash = false;
        foreach (var material in authoring.sharedMaterials)
        {
            if (material == null) continue;
            DependsOn(material);
            if (material.HasProperty(HitFlashProperty)) declaresFlash = true;
        }
        if (!declaresFlash)
            return;

        var entity = GetEntity(TransformUsageFlags.Renderable);
        AddComponent(entity, new HitFlash { Value = 0f });
        SetComponentEnabled<HitFlash>(entity, false);
    }
}
