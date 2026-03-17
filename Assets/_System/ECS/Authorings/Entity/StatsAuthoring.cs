using Unity.Entities;
using UnityEngine;

public class StatsAuthoring : MonoBehaviour
{
    [Header("Stats")] public CoreStats baseStats;
    
    private class Baker : Baker<StatsAuthoring>
    {
        public override void Bake(StatsAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, authoring.baseStats);
        }
    }
    
// #if UNITY_EDITOR
//
//     [Button]
//     private void LoadFromAsset()
//     {
//         
//     }
//     
// #endif
}
