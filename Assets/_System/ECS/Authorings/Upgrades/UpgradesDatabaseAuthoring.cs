using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class UpgradesDatabaseAuthoring : MonoBehaviour
{
    [Tooltip("Global Stats Upgrades (Speed, Health...)")]
    public UpgradesDatabaseSO GameStatUpgradeslDatabase;

    [Tooltip("All Character Specific Upgrades (Unlock Fireball, Upgrade Meteor...)")]
    public UpgradesDatabaseSO[] CharacterSpellUpgradeslDatabases;

    private class Baker : Baker<UpgradesDatabaseAuthoring>
    {
        public override void Bake(UpgradesDatabaseAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            List<UpgradeSO> allUpgrades = new List<UpgradeSO>();

            // Stats Upgrades
            if (authoring.GameStatUpgradeslDatabase != null)
            {
                foreach (var up in authoring.GameStatUpgradeslDatabase.Upgrades)
                {
                    if (up != null) allUpgrades.Add(up);
                }
            }

            // Characters Upgrades
            if (authoring.CharacterSpellUpgradeslDatabases != null)
            {
                foreach (var db in authoring.CharacterSpellUpgradeslDatabases)
                {
                    if (db == null)
                        continue;

                    foreach (var up in db.Upgrades)
                    {
                        if (up != null && !allUpgrades.Contains(up))
                            allUpgrades.Add(up);
                    }
                }
            }

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
