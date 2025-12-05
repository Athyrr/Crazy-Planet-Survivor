using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

public class DamageOnContactAuthoring : MonoBehaviour
{
    class Baker : Baker<DamageOnContactAuthoring>
    {
        public override void Bake(DamageOnContactAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<DamageOnContact>(entity);
        }
    }
}
