using Unity.Entities;
using UnityEngine;

/// <summary>
/// Ajoute HitFrameColor (desactive) sur l'entite du renderer principal de l'ennemi :
/// c'est elle qui porte la MaterialProperty _PowerHit, pas la racine.
/// </summary>
public class EnemyHitFlashBaker : Baker<MeshRenderer>
{
    public override void Bake(MeshRenderer authoring)
    {
        var enemy = GetComponentInParent<EnemyAuthoring>();
        if (enemy == null || enemy.MainRenderer != authoring)
            return;

        var entity = GetEntity(TransformUsageFlags.Renderable);
        AddComponent(entity, new HitFrameFeedbackSystem.HitFrameColor { Value = 0 });
        SetComponentEnabled<HitFrameFeedbackSystem.HitFrameColor>(entity, false);
    }
}
