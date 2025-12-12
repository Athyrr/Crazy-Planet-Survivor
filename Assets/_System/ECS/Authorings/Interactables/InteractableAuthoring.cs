using Mono.Cecil;
using Unity.Entities;
using UnityEngine;

public class InteractableAuthoring : MonoBehaviour
{
    [SerializeField]
    private EInteractionType InteractionType;

    [SerializeField]
    private float InteractionRadius;

    private class Baker : Baker<InteractableAuthoring>
    {
        public override void Bake(InteractableAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.WorldSpace);

            AddComponent(entity, new Interactable
            {
                InteractionType = authoring.InteractionType,
                Radius = authoring.InteractionRadius
            });

            AddComponent<InteractableInRangeTag>(entity);
            SetComponentEnabled<InteractableInRangeTag>(entity, false);
        }
    }
}
