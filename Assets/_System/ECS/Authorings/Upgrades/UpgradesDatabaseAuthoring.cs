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

                // Stat Player (rarity + one or more bonus/malus modifiers)
                if (upgradeSO is StatUpgradeSO statUpgrade)
                {
                    blob.Rarity = statUpgrade.Rarity;
                    blob.RequiredSpellTag = statUpgrade.RequiredSpellTag;

                    var modifiers = statUpgrade.Modifiers;
                    int modCount = modifiers != null ? modifiers.Length : 0;
                    BlobBuilderArray<StatModifierBlob> modArray =
                        builder.Allocate(ref blob.StatModifiers, modCount);

                    for (int m = 0; m < modCount; m++)
                    {
                        modArray[m] = new StatModifierBlob
                        {
                            CharacterStat = modifiers[m].CharacterStat,
                            Strategy = modifiers[m].Strategy,
                            Value = modifiers[m].Value,
                        };
                    }
                }
                // Spell Upgrade Logic (unlock or effect upgrade) — not subject to rarity
                else if (upgradeSO is SpellUpgradeSO spellUpgrade)
                {
                    builder.Allocate(ref blob.StatModifiers, 0);

                    blob.SpellID = spellUpgrade.SpellID;
                    blob.SpellTags = spellUpgrade.RequiredTags;
                    blob.SpellStat = spellUpgrade.SpellStat;

                    // Spell upgrades keep a single value/strategy.
                    blob.ModifierStrategy = upgradeSO.ModifierStrategy;
                    blob.Value = upgradeSO.Value;
                }
                else
                {
                    builder.Allocate(ref blob.StatModifiers, 0);
                }
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
