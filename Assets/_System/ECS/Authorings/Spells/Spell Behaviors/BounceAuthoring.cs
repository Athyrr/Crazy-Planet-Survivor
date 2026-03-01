using Unity.Entities;
using UnityEngine;

public class BounceAuthoring : MonoBehaviour
{
    class Baker : Baker<BounceAuthoring>
    {
        public override void Bake(BounceAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Bounce>(entity);

            AddComponent(entity, new FollowTargetMovement
            {
                Target = Entity.Null,
                Speed = 0
            });

            SetComponentEnabled<FollowTargetMovement>(entity, false);
        }
    }
}