using Unity.Entities;
using UnityEngine;

public class InteractableAuthoring : MonoBehaviour
{
    [SerializeField]
    private EInteractionType _interactionType;

    [SerializeField]
    private float _interactionRadius;

    private class Baker : Baker<InteractableAuthoring>
    {
        public override void Bake(InteractableAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.WorldSpace);

            AddComponent(entity, new Interactable
            {
                InteractionType = authoring._interactionType,
                Radius = authoring._interactionRadius
            });

            AddComponent<InteractableInRangeTag>(entity);
            SetComponentEnabled<InteractableInRangeTag>(entity, false);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, _interactionRadius);
    }
}
