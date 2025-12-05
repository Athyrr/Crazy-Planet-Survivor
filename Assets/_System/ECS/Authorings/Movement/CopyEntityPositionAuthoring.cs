using Unity.Entities;
using UnityEngine;

public class CopyEntityPositionAuthoring : MonoBehaviour
{
    class Baker : Baker<CopyEntityPositionAuthoring>
    {
        public override void Bake(CopyEntityPositionAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<CopyEntityPosition>(entity);
        }
    }
}
