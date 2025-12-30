#if ENABLE_STATISTICS
using Unity.Entities;
using UnityEngine;

public class GameStatisticsAuthoring : MonoBehaviour
{
    class Baker : Baker<GameStatisticsAuthoring>
    {
        public override void Bake(GameStatisticsAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new GameStatistics());
        }
    }
}
#endif
