using _System.ECS.Authorings.Resources;
using Unity.Entities;
using UnityEngine;

public class ResourceAuthoring : MonoBehaviour
{
    public EResourceType RessourceType;
    [Tooltip("If true, the orb will snap perfectly to the ground when attracted.")]
    public bool HardSnapToGround;

    private class Baker : Baker<ResourceAuthoring>
    {
        public override void Bake(ResourceAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<LootTag>(entity);

            AddComponent(entity, new Resource { Type = authoring.RessourceType });

            AddComponent(entity, new RunScope());

            AddComponent(entity, new DestroyEntityFlag());
            SetComponentEnabled<DestroyEntityFlag>(entity, false);

            if (authoring.HardSnapToGround)
                AddComponent<HardSnappedMovement>(entity);
        }
    }
}