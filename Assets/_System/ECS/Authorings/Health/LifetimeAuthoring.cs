using Unity.Entities;
using UnityEngine;

public class HealtAuthoring : MonoBehaviour
{
    class Baker : Baker<HealtAuthoring>
    {
        public override void Bake(HealtAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Health>(entity);
        }
    }
}
