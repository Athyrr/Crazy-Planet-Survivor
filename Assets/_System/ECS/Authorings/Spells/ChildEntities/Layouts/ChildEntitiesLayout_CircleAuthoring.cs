using Unity.Entities;
using UnityEngine;

public class ChildEntitiesLayout_CircleAuthoring : MonoBehaviour
{
    private class Baker : Baker<ChildEntitiesLayout_CircleAuthoring>
    {
        public override void Bake(ChildEntitiesLayout_CircleAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<ChildEntitiesLayout_Circle>(entity);
        }
    }
}
