using Unity.Entities;
using UnityEngine;

public class HealthAuthoring : MonoBehaviour
{
    [SerializeField] private int _defaultValue;
    
    private class Baker : Baker<HealthAuthoring>
    {
        public override void Bake(HealthAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Health
            {
                Value = authoring._defaultValue
            });
        }
    }
}
