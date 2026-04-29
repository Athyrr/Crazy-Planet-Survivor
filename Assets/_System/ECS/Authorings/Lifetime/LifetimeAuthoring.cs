using Unity.Entities;
using UnityEngine;

[RequireComponent(typeof(DestructibleAuthoring))]
public class LifetimeAuthoring : MonoBehaviour
{

    public bool OverrideData = false;
    public float Lifetime;
    class Baker : Baker<LifetimeAuthoring>
    {
        public override void Bake(LifetimeAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Lifetime>(entity);

            if (authoring.OverrideData)
            {
                SetComponent(entity, new Lifetime { Duration = authoring.Lifetime, TimeLeft = authoring.Lifetime});
            }
        }
    }
}
