using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class UpgradesDatabaseAuthoring : MonoBehaviour
{
    public UpgradesDatabaseSO UpgradesDatabase;

    private class Baker : Baker<UpgradesDatabaseAuthoring>
    {
        public override void Bake(UpgradesDatabaseAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            // Create upgrade blobs for data
            var builder = new BlobBuilder(Allocator.Persistent);

            ref UpgradeBlobs root = ref builder.ConstructRoot<UpgradeBlobs>();
            int databaseLength = authoring.UpgradesDatabase.Upgrades.Length;
            BlobBuilderArray<UpgradeBlob> arrayBuilder = builder.Allocate(ref root.Upgrades, databaseLength);

            for (int i = 0; i < databaseLength; i++)
            {
                UpgradeDataSO upgradeSO = authoring.UpgradesDatabase.Upgrades[i];

                if (upgradeSO == null)
                    continue;

                ref UpgradeBlob upgradeBlobRoot = ref arrayBuilder[i];

                //upgradeBlobRoot.DisplayName = upgradeSO.DisplayName;
                //upgradeBlobRoot.Description = upgradeSO.Description;
                upgradeBlobRoot.UpgradeType = upgradeSO.UpgradeType;
                upgradeBlobRoot.StatType = upgradeSO.Stat;
                upgradeBlobRoot.ModifierStrategy = upgradeSO.ModifierStrategy;
                upgradeBlobRoot.Value = upgradeSO.Value;
                upgradeBlobRoot.SpellID = upgradeSO.Spell;
            }

            var upgradesDatabaseBlob = builder.CreateBlobAssetReference<UpgradeBlobs>(Allocator.Persistent);
            AddComponent(entity, new UpgradesDatabase { Blobs = upgradesDatabaseBlob });

            builder.Dispose();


            // log database length
            Debug.Log("Upgrade database length: " + upgradesDatabaseBlob.Value.Upgrades.Length);

            // Add upgrades prefab to buffer  
            //@todo new Buffer element UpgradeUIPrefab to store upgrade UI card prefab for selection.
            //@todo check if sending request from dots world to gameObject world worst it.
            //var prefabBuffer = AddBuffer<UpgradeUIPrefab>(entity);
            //foreach (var upgradeSO in authoring.UpgradesDatabase.Upgrades)
            //{
            //    if (upgradeSO == null || upgradeSO.UIPrefab == null)
            //        continue;

            //    prefabBuffer.Add(new UpgradeUIPrefab
            //    {
            //        Prefab = GetEntity(upgradeSO.UIPrefab, TransformUsageFlags.Dynamic)
            //    });
            //}
        }
    }
}
