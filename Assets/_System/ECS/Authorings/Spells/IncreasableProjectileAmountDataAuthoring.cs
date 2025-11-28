using Unity.Entities;
using UnityEngine;

class IncreasableProjectileAmountDataAuthoring : MonoBehaviour
{
    class IncreasableProjectileAmountDataBaker : Baker<IncreasableProjectileAmountDataAuthoring>
    {
        public override void Bake(IncreasableProjectileAmountDataAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<IncreasableProjectileAmountData>(entity);
        }
    }
}