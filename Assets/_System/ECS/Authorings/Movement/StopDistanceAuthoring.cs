using Unity.Entities;
using UnityEngine;

public class StopDistanceAuthoring : MonoBehaviour
{
    public float Distance = 0;

    private class Baker : Baker<StopDistanceAuthoring>
    {
        public override void Bake(StopDistanceAuthoring authoring)
        {
            var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);

            AddComponent(entity, new StopDistance()
            {
                Distance = authoring.Distance,
            });
        }
    }
}
