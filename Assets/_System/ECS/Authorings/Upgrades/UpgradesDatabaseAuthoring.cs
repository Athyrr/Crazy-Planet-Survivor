using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class UpgradesDatabaseAuthoring : MonoBehaviour
{
    [Tooltip("Reference to the global upgrades config.")]
    public GameUpgradesConfigSO GlobalConfig;

    private class Baker : Baker<UpgradesDatabaseAuthoring>
    {
        public override void Bake(UpgradesDatabaseAuthoring authoring)
        {
            if (authoring.GlobalConfig == null) 
                return;

            Entity entity = GetEntity(TransformUsageFlags.None);

            // All game upgrades in one list
            List<UpgradeSO> allUpgrades = authoring.GlobalConfig.GetFlattenedUpgrades();

            // Create upgrade blobs for data
            var builder = new BlobBuilder(Allocator.Temp);
            ref UpgradeBlobs root = ref builder.ConstructRoot<UpgradeBlobs>();

            int count = allUpgrades.Count;
            BlobBuilderArray<UpgradeBlob> arrayBuilder = builder.Allocate(ref root.Upgrades, count);

            for (int i = 0; i < count; i++)
            {
                UpgradeSO upgradeSO = allUpgrades[i];
                ref UpgradeBlob blob = ref arrayBuilder[i];

                builder.AllocateString(ref blob.DisplayName, upgradeSO.DisplayName);
                builder.AllocateString(ref blob.Description, upgradeSO.Description);

                blob.UpgradeType = upgradeSO.UpgradeType;

                // Stat Player
                blob.CharacterStat = upgradeSO.CharacterStat;

                // Spell Upgrade Logic
                blob.SpellID = upgradeSO.SpellID;
                blob.SpellTags = upgradeSO.RequiredTags;
                blob.SpellStat = upgradeSO.SpellStat;

                // Value
                blob.ModifierStrategy = upgradeSO.ModifierStrategy;
                blob.Value = upgradeSO.Value;
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
