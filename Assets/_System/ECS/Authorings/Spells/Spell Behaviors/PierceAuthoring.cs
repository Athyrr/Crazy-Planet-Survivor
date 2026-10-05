using Unity.Entities;

public class PierceAuthoring : SpellCapabilityAuthoring
{
    class Baker : Baker<PierceAuthoring>
    {
        public override void Bake(PierceAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            BakeDisabled<Pierce>(this, entity);
        }
    }
}
