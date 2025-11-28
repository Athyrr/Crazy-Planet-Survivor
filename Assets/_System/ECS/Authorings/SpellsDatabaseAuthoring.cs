using Unity.Collections;
using Unity.Entities;
using Unity.Entities.UniversalDelegates;
using UnityEngine;


public class SpellsDatabaseAuthoring : MonoBehaviour
{
    public SpellDatabaseSO SpellDatabase;

    private class Baker : Baker<SpellsDatabaseAuthoring>
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="authoring"></param>
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
                spellBlobRoot.Bounces = spellSO.Bounces;
                spellBlobRoot.BouncesSearchRadius = spellSO.BouncesSearchRadius;
                spellBlobRoot.InstanciateOnce = spellSO.InstantiateOnce;
            }

            var spellsDatabaseBlob = builder.CreateBlobAssetReference<SpellBlobs>(Allocator.Persistent);
            AddComponent(entity, new SpellsDatabase { Blobs = spellsDatabaseBlob });

            // Register blob asset (auto free memory)
            AddBlobAsset(ref spellsDatabaseBlob, out var hash);

            // Dispose builder
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

            // Create entity for spell to index map
            //int dbLength = authoring.SpellDatabase.Spells.Length;
            //var spellIndexMap = CreateAdditionalEntity(TransformUsageFlags.None, false, nameof(SpellToIndexMap));

            //NativeHashMap<SpellKey, int> map = new NativeHashMap<SpellKey, int>(dbLength, Allocator.Persistent);
            //for (int i = 0; i < dbLength; i++)
            //{
            //    var spellData = authoring.SpellDatabase.Spells[i];
            //    map.TryAdd(new SpellKey { Value = spellData.ID }, i);
            //}
            //AddComponent(spellIndexMap, new SpellToIndexMap { Map = map });
        }
    }
}