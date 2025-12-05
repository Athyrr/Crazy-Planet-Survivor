using Unity.Entities;
using UnityEngine;

public class SelfRotateAuthoring : MonoBehaviour
{
    private class Baker : Baker<SelfRotateAuthoring>
    {
        public override void Bake(SelfRotateAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<SelfRotate>(entity);
        }
    }
}
