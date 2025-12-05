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
            var builder = new BlobBuilder(Allocator.Temp);

            ref UpgradeBlobs root = ref builder.ConstructRoot<UpgradeBlobs>();
            int databaseLength = authoring.UpgradesDatabase.Upgrades.Length;
            BlobBuilderArray<UpgradeBlob> arrayBuilder = builder.Allocate(ref root.Upgrades, databaseLength);

            for (int i = 0; i < databaseLength; i++)
            {
                UpgradeDataSO upgradeSO = authoring.UpgradesDatabase.Upgrades[i];

                if (upgradeSO == null)
                    continue;

                ref UpgradeBlob upgradeBlobRoot = ref arrayBuilder[i];

                builder.AllocateString(ref upgradeBlobRoot.DisplayName, upgradeSO.DisplayName);
                builder.AllocateString(ref upgradeBlobRoot.Description, upgradeSO.Description);

                upgradeBlobRoot.UpgradeType = upgradeSO.UpgradeType;
                upgradeBlobRoot.Stat = upgradeSO.Stat;
                upgradeBlobRoot.ModifierStrategy = upgradeSO.ModifierStrategy;
                upgradeBlobRoot.Value = upgradeSO.Value;
                upgradeBlobRoot.SpellID = upgradeSO.SpellID;
            }

            var upgradesDatabaseBlob = builder.CreateBlobAssetReference<UpgradeBlobs>(Allocator.Persistent);
            AddComponent(entity, new UpgradesDatabase { Blobs = upgradesDatabaseBlob });

            // Register blob asset (auto free memory)
            AddBlobAsset(ref upgradesDatabaseBlob, out var hash);

            // Dispose builder
            builder.Dispose();          
        }
    }
}
