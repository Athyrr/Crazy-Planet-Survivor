using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class OrbsDatabaseAuthoring : MonoBehaviour
{
    public ExperienceOrbAuthoring[] OrbPrefabs;

    private class Baker : Baker<OrbsDatabaseAuthoring>
    {
        public override void Bake(OrbsDatabaseAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            var buffer = AddBuffer<OrbDatabaseBufferElement>(entity);

            var orderedOrbs = new NativeList<OrbDatabaseBufferElement>(Allocator.Temp);

            foreach (var orbPrefab in authoring.OrbPrefabs)
            {
                if (orbPrefab == null)
                    continue;

                orderedOrbs.Add(new OrbDatabaseBufferElement
                {
                    Prefab = GetEntity(orbPrefab.gameObject, TransformUsageFlags.Dynamic),
                    Value = orbPrefab.Value
                });
            }

            // Sort by descending
            orderedOrbs.Sort();

            // Set buffer
            buffer.AddRange(orderedOrbs.AsArray());

            orderedOrbs.Dispose();  
        }
    }
}