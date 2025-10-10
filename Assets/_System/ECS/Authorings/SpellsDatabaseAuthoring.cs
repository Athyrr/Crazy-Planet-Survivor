using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using UnityEngine;


public class SpellsDatabaseAuthoring : MonoBehaviour
{
    public SpellDatabaseSO SpellDatabase;

    private class Baker : Baker<SpellsDatabaseAuthoring>
    {
        public override void Bake(SpellsDatabaseAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            // Create spell blobs for data
            //BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            var builder = new BlobBuilder(Allocator.Persistent);

            ref var root = ref builder.ConstructRoot<SpellBlobs>();
            BlobBuilderArray<SpellBlob> arrayBuilder = builder.Allocate(ref root.Spells, authoring.SpellDatabase.Spells.Length);

            for (int i = 0; i < authoring.SpellDatabase.Spells.Length; i++)
            {
                SpellDataSO spellSO = authoring.SpellDatabase.Spells[i];

                if (spellSO == null)
                    continue;

                ref var spellBlobRoot = ref arrayBuilder[i];

                spellBlobRoot.ID = spellSO.ID;
                spellBlobRoot.BaseCooldown = spellSO.BaseCooldown;
                spellBlobRoot.BaseDamage = spellSO.BaseDamage;
                spellBlobRoot.BaseArea = spellSO.BaseArea;
                spellBlobRoot.BaseRange = spellSO.BaseRange;
                spellBlobRoot.BaseSpeed = spellSO.BaseSpeed;
                spellBlobRoot.Element = spellSO.Element;
                spellBlobRoot.Lifetime = spellSO.Lifetime;
            }

            var spellsDatabaseBlob = builder.CreateBlobAssetReference<SpellBlobs>(Allocator.Persistent);
            AddComponent(entity, new SpellsDatabase { Blobs = spellsDatabaseBlob });

            builder.Dispose();


            // Add spell prefabs to buffer  
            var prefabBuffer = AddBuffer<SpellPrefab>(entity);
            foreach (var spellSO in authoring.SpellDatabase.Spells)
            {
                if (spellSO == null || spellSO.SpellPrefab == null)
                    continue;

                prefabBuffer.Add(new SpellPrefab
                {
                    Prefab = GetEntity(spellSO.SpellPrefab, TransformUsageFlags.Dynamic)
                });
            }

        }
    }
}