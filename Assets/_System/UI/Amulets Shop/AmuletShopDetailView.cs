using PrimeTween;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class AmuletShopDetailView : ShopDetailViewBase<AmuletSO>
{
    [Header("Preview")]
    public Transform AmuletPreviewContainer;
    public GameObject DefaultAmulet;

    [Header("Overlapped Detail Containers")]
    public GameObject CostContainer;

    public GameObject InfoContainer;

    [Header("Cost Display (locked)")]
    public ResourceWidgetItem CostItemPrefab;
    public ResourceDatabaseSO ResourceDatabase;

    [Header("Description Display (unlocked)")]
    public TMP_Text DescriptionText;

    public TMP_Text EffectsText;

    [Header("Detail Texts")]
    public TMP_Text AmuletNameText;

    [Header("Default Strings")]
    public string DefaultName = "???";

    [Header("NFC Purchase VFX")]
    public GameObject UnlockVfxPrefab;
    public Vector3 VfxLocalOffset = new Vector3(0f, 0f, 1.5f);
    public float VfxLifetime = 8f;
    public float SpawnDelay = 1.05f;
    public Vector3 AnimScale = new Vector3(200f, 200f, 200f);

    public void PlayUnlockVfx()
    {
        if (UnlockVfxPrefab == null || AmuletPreviewContainer == null)
            return;

        var vfx = Instantiate(UnlockVfxPrefab);
        vfx.transform.localPosition = AmuletPreviewContainer.transform.position + VfxLocalOffset;
        vfx.transform.localRotation =
            AmuletPreviewContainer.transform.rotation * Quaternion.identity;
        vfx.transform.localScale = AnimScale;
        SetLayerRecursive(vfx, AmuletPreviewContainer.gameObject.layer);

        vfx.GetComponent<ParticleSystem>().Play();

        if (VfxLifetime > 0f)
            Destroy(vfx, VfxLifetime);
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

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
        if (DefaultAmulet != null)
            Instantiate(DefaultAmulet, AmuletPreviewContainer);

        if (AmuletNameText != null)
            AmuletNameText.text = DefaultName;

        // Cost container active and info container hidden
        if (CostContainer != null)
            CostContainer.SetActive(true);
        if (InfoContainer != null)
            InfoContainer.SetActive(false);

        ShowCost(data.PurchaseCost);
    }

    private void ShowUnlocked(AmuletSO data)
    {
        if (DefaultAmulet != null)
            Instantiate(DefaultAmulet, AmuletPreviewContainer);

        Tween.Delay(
            duration: SpawnDelay,
            () =>
            {
                if (DefaultAmulet != null)
                {
                    foreach (Transform child in AmuletPreviewContainer.transform)
                    {
                        if (child.gameObject != UnlockVfxPrefab.gameObject)
                        {
                            Destroy(child.gameObject);
                        }
                    }
                }
                if (data.UIPrefab != null)
                    Instantiate(data.UIPrefab, AmuletPreviewContainer);
            }
        );

        // if (data.UIPrefab != null)
        //     Instantiate(data.UIPrefab, AmuletPreviewContainer);

        if (AmuletNameText != null)
            AmuletNameText.text = data.DisplayName;

        // Info container active and cost container hidden
        if (InfoContainer != null)
            InfoContainer.SetActive(true);
        if (CostContainer != null)
            CostContainer.SetActive(false);

        if (DescriptionText != null)
            DescriptionText.text = data.Description;
        if (EffectsText != null)
            EffectsText.text = FormatEffects(data.Modifiers);
    }

    private void ShowCost(ResourceCost[] costs)
    {
        if (CostContainer == null || CostItemPrefab == null || ResourceDatabase == null)
            return;

        if (costs == null || costs.Length == 0)
            return;

        var parent = CostContainer.transform;
        foreach (var cost in costs)
        {
            var resource = ResourceDatabase.GetResource(cost.Type);
            var instance = Instantiate(CostItemPrefab, parent);
            instance.Refresh(cost.Type, resource != null ? resource.Icon : null, cost.Amount);
        }
    }

    private static string FormatEffects(AmuletModifier[] modifiers)
    {
        if (modifiers == null || modifiers.Length == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var mod in modifiers)
            sb.AppendLine(FormatModifier(mod));

        return sb.ToString().TrimEnd('\n', '\r');
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

        string formattedValue;
        bool isFlatBonus =
            mod.CharacterStat is ECharacterStat.PierceCount or ECharacterStat.BounceCount;

        if (isFlatBonus)
        {
            // Integer bonuses: +2, -1
            formattedValue = $"{(int)mod.Value:+0;-0;0}";
        }
        else
        {
            // Percentage bonuses: +50%, -10%
            formattedValue = $"{mod.Value:+0%;-0%;0}";
        }

        // Set color green for positive, red for negative
        string color = mod.Value >= 0 ? "#4ADE80" : "#F87171";
        return $"<color={color}>{formattedValue}</color> {target}";
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
            foreach (Transform child in CostContainer.transform)
                Destroy(child.gameObject);
        }

        // Reset visibility
        if (CostContainer != null)
            CostContainer.SetActive(false);
        if (InfoContainer != null)
            InfoContainer.SetActive(false);
    }
}
