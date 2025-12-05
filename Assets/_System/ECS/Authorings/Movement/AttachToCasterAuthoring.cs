using Unity.Entities;
using UnityEngine;

public class AttachToCasterAuthoring : MonoBehaviour
{
    class Baker : Baker<AttachToCasterAuthoring>
    {
        public override void Bake(AttachToCasterAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<AttachToCaster>(entity);
        }
    }
}
