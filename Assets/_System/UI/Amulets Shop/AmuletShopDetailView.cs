using TMPro;
using UnityEngine;

public class AmuletShopDetailView : ShopDetailViewBase<AmuletSO>
{
    [Header("Preview Container")] public Transform AmuletPreviewContainer;
    public GameObject DefaultAmulet;

    [Header("Cost Container")] public Transform CostContainer;
    public ResourceWidgetItem CostItemPrefab;

    [Header("Effects Container")] public Transform EffectsContainer;
    public TMP_Text EffectsText;

    [Header("Database")] public ResourceDatabaseSO ResourceDatabase;

    [Header("Detail Texts")] public TMP_Text AmuletNameText;
    public TMP_Text AmuletDescriptionText;

    [Header("Default Strings")] public string DefaultName = "Locked";
    public string DefaultDescription = "";

    public void Refresh(AmuletSO data, bool isUnlocked)
    {
        Clear();

        if (data == null || AmuletPreviewContainer == null)
            return;

        if (!isUnlocked)
        {
            ShowLocked(data);
            return;
        }

        ShowUnlocked(data);
    }

    private void ShowLocked(AmuletSO data)
    {
        // Silhouette preview
        if (DefaultAmulet != null && AmuletPreviewContainer != null)
            Instantiate(DefaultAmulet, AmuletPreviewContainer);

        if (AmuletNameText != null) AmuletNameText.text = DefaultName;
        if (AmuletDescriptionText != null) AmuletDescriptionText.text = DefaultDescription;

        // Cost breakdown
        ShowCost(data.PurchaseCost);
    }

    private void ShowUnlocked(AmuletSO data)
    {
        // Real amulet preview
        if (data.UIPrefab != null && AmuletPreviewContainer != null)
            Instantiate(data.UIPrefab, AmuletPreviewContainer);

        if (AmuletNameText != null) AmuletNameText.text = data.DisplayName;
        if (AmuletDescriptionText != null) AmuletDescriptionText.text = data.Description;

        // Effects
        ShowEffects(data.Modifiers);
    }

    private void ShowCost(ResourceCost[] costs)
    {
        if (CostContainer == null || CostItemPrefab == null || ResourceDatabase == null)
            return;

        if (costs == null || costs.Length == 0)
            return;

        foreach (var cost in costs)
        {
            var resource = ResourceDatabase.GetResource(cost.Type);
            var instance = Instantiate(CostItemPrefab, CostContainer);
            instance.Refresh(cost.Type, resource != null ? resource.Icon : null, cost.Amount);
        }
    }

    private void ShowEffects(AmuletModifier[] modifiers)
    {
        if (EffectsContainer == null || EffectsText == null)
            return;

        if (modifiers == null || modifiers.Length == 0)
        {
            EffectsText.text = string.Empty;
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var mod in modifiers)
            sb.AppendLine(FormatModifier(mod));

        EffectsText.text = sb.ToString().TrimEnd('\n', '\r');
    }

    private static string FormatModifier(AmuletModifier mod)
    {
        string target;
        if (mod.CharacterStat != ECharacterStat.None)
            target = mod.CharacterStat.ToString();
        else if (mod.SpellStat != ESpellStat.None)
            target = mod.SpellStat.ToString();
        else if (mod.SpellTags != ESpellTag.None)
            target = mod.SpellTags.ToString();
        else if (mod.SpellID != ESpellID.None)
            target = mod.SpellID.ToString();
        else
            target = mod.UpgradeType.ToString();

        string prefix = mod.Strategy switch
        {
            EModiferStrategy.Flat => "+",
            EModiferStrategy.Multiply => "x",
            _ => ""
        };

        return $"{prefix}{mod.Value} {target}";
    }

    public void Clear()
    {
        // Clear preview
        if (AmuletPreviewContainer != null)
        {
            foreach (Transform child in AmuletPreviewContainer)
                Destroy(child.gameObject);
        }

        // Clear cost items
        if (CostContainer != null)
        {
            foreach (Transform child in CostContainer)
                Destroy(child.gameObject);
        }

        // Clear effects
        if (EffectsContainer != null)
        {
            foreach (Transform child in EffectsContainer)
                Destroy(child.gameObject);
        }
    }
}