using Unity.Entities;
using UnityEngine;

public class ChildEntitiesLayout_CircleAuthoring : MonoBehaviour
{
    private class Baker : Baker<ChildEntitiesLayout_CircleAuthoring>
    {
        public override void Bake(ChildEntitiesLayout_CircleAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new SubSpellsLayout_Circle
            {
                Radius = 0, 
                AngleInDegrees = 360
            });
        }
    }
}
