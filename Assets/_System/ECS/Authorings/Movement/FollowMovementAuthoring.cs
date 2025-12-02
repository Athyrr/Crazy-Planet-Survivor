using Unity.Entities;
using UnityEngine;

public class FollowMovementAuthoring : MonoBehaviour
{
    [SerializeField]
    public bool EnabledOnInit = true;

    class Baker : Baker<FollowMovementAuthoring>
    {
        public override void Bake(FollowMovementAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<FollowTargetMovement>(entity);
            SetComponentEnabled<FollowTargetMovement>(entity, authoring.EnabledOnInit);
        }
    }
}
