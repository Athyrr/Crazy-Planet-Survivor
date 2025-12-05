using Unity.Collections;
using Unity.Entities;
using UnityEngine;


public class SpellsDatabaseAuthoring : MonoBehaviour
{
    public SpellDatabaseSO SpellDatabase;

    private class Baker : Baker<SpellsDatabaseAuthoring>
    {
        public override void Bake(SpellsDatabaseAuthoring authoring)
        {
            Entity dbEntity = GetEntity(TransformUsageFlags.None);

            var mainPrefabBuffer = AddBuffer<SpellPrefab>(dbEntity);
            var childPrefabBuffer = AddBuffer<ChildSpellPrefab>(dbEntity);

            // Create spell blobs for data
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SpellBlobs>();

            int count = authoring.SpellDatabase.Spells.Length;
            BlobBuilderArray<SpellBlob> arrayBuilder = builder.Allocate(ref root.Spells, count);

            for (int i = 0; i < count; i++)
            {
                SpellDataSO spellSO = authoring.SpellDatabase.Spells[i];

                if (spellSO == null)
                    continue;

                ref var spellBlob = ref arrayBuilder[i];

                spellBlob.ID = spellSO.ID;
                spellBlob.BaseCooldown = spellSO.BaseCooldown;
                spellBlob.BaseDamage = spellSO.BaseDamage;
                spellBlob.BaseEffectArea = spellSO.BaseEffectArea;
                spellBlob.BaseCastRange = spellSO.BaseCastRange;
                spellBlob.BaseSpeed = spellSO.BaseSpeed;
                spellBlob.Element = spellSO.Element;
                spellBlob.Lifetime = spellSO.Lifetime;
                spellBlob.BouncesSearchRadius = spellSO.BouncesSearchRadius;
                spellBlob.BaseSpawnOffset = spellSO.BaseSpawnOffset;

                spellBlob.Bounces = spellSO.Bounces;
                spellBlob.Pierces = spellSO.Pierces;

                // Tick effects (for auras)
                spellBlob.BaseDamagePerTick = spellSO.BaseDamagePerTick;
                spellBlob.TickRate = spellSO.TickRate;

                // Children based spells
                spellBlob.ChildrenCount = spellSO.ChildrenCount;
                spellBlob.ChildrenSpawnRadius = spellSO.ChildrenSpawnRadius;

                if (spellSO.SpellPrefab != null)
                {
                    var mainEntity = GetEntity(spellSO.SpellPrefab, TransformUsageFlags.Dynamic);
                    mainPrefabBuffer.Add(new SpellPrefab { Prefab = mainEntity });
                }
                else
                {
                    mainPrefabBuffer.Add(new SpellPrefab { Prefab = Entity.Null });
                }

                if (spellSO.ChildPrefab != null)
                {
                    var childEntity = GetEntity(spellSO.ChildPrefab, TransformUsageFlags.Dynamic);
                    childPrefabBuffer.Add(new ChildSpellPrefab { Prefab = childEntity });
                    spellBlob.ChildPrefabIndex = childPrefabBuffer.Length - 1;
                }
                else
                {
                    spellBlob.ChildPrefabIndex = -1;
                }
            }

            var spellsDatabaseBlob = builder.CreateBlobAssetReference<SpellBlobs>(Allocator.Persistent);
           
            AddComponent(dbEntity, new SpellsDatabase { Blobs = spellsDatabaseBlob });

            // Register blob asset (auto free memory)
            AddBlobAsset(ref spellsDatabaseBlob, out var hash);

            // Dispose builder
            builder.Dispose();

        }
    }
}