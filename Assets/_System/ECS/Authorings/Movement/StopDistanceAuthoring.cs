using Unity.Entities;
using UnityEngine;

public class StopDistanceAuthoring : MonoBehaviour
{
    [Tooltip("Range the entity holds from the player: it stops advancing once the player is within this. " +
             "For a ranged enemy, set it to roughly its spell's cast range so it stops where it can fire.")]
    public float Distance = 0;

    [Tooltip("Range below which the entity retreats from the player (0 = never retreats, plain stop-and-hold). " +
             "Keep it below Distance; the gap between the two is the band it settles in. > 0 makes a ranged kiter.")]
    public float RetreatDistance = 0;

    private class Baker : Baker<StopDistanceAuthoring>
    {
        public override void Bake(StopDistanceAuthoring authoring)
        {
            var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);

            AddComponent(entity, new StopDistance()
            {
                Distance = authoring.Distance,
                RetreatDistance = authoring.RetreatDistance,
            });
        }
    }
}
