using Unity.Entities;

public class BounceAuthoring : SpellCapabilityAuthoring
{
    class Baker : Baker<BounceAuthoring>
    {
        public override void Bake(BounceAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            BakeDisabled<Bounce>(this, entity);

            AddComponent(entity, new FollowTargetMovement
            {
                Target = Entity.Null,
                Speed = 0
            });

            SetComponentEnabled<FollowTargetMovement>(entity, false);
        }
    }
}
