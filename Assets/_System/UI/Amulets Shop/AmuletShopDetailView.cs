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
    [Tooltip("Locked-state panel, toggled on when the amulet is locked. Assign the 'Locked View' object (the parent of the Cost Container), NOT the inner cost container.")]
    public GameObject LockedView;

    [Tooltip("Container with the layout group where cost item widgets are spawned (inside Locked View).")]
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

    /// <summary>
    /// Refreshes the detail view. <paramref name="animateUnlock"/> should only be true when the
    /// amulet was just purchased: it plays the delayed reveal (base character shown first, then
    /// the amulet after <see cref="SpawnDelay"/>). On plain selection switches it must be false so
    /// the amulet appears immediately with no base-character reshow.
    /// </summary>
    public void Refresh(AmuletSO data, bool isUnlocked, bool animateUnlock = false)
    {
        Clear();

        if (data == null || AmuletPreviewContainer == null)
            return;

        if (!isUnlocked)
        {
            ShowLocked(data);
            return;
        }

        ShowUnlocked(data, animateUnlock);
    }

    private void ShowLocked(AmuletSO data)
    {
        if (DefaultAmulet != null)
            Instantiate(DefaultAmulet, AmuletPreviewContainer);

        if (AmuletNameText != null)
            AmuletNameText.text = DefaultName;

        // Locked panel visible (cost + purchase), info container hidden.
        var lockedView = ResolveLockedView();
        if (lockedView != null)
            lockedView.SetActive(true);
        if (CostContainer != null)
            CostContainer.SetActive(true);
        if (InfoContainer != null)
            InfoContainer.SetActive(false);

        ShowCost(data.PurchaseCost);
    }

    private void ShowUnlocked(AmuletSO data, bool animateUnlock)
    {
        if (animateUnlock)
        {
            // Purchase reveal: show the base character first, then swap in the amulet after a delay.
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
                            if (UnlockVfxPrefab == null
                                || child.gameObject != UnlockVfxPrefab.gameObject)
                            {
                                Destroy(child.gameObject);
                            }
                        }
                    }
                    if (data.UIPrefab != null)
                        Instantiate(data.UIPrefab, AmuletPreviewContainer);
                }
            );
        }
        else
        {
            // Plain selection switch: show the amulet immediately, no base-character reshow.
            if (data.UIPrefab != null)
                Instantiate(data.UIPrefab, AmuletPreviewContainer);
        }

        if (AmuletNameText != null)
            AmuletNameText.text = data.DisplayName;

        // Info container active and locked panel hidden
        if (InfoContainer != null)
            InfoContainer.SetActive(true);
        var lockedView = ResolveLockedView();
        if (lockedView != null)
            lockedView.SetActive(false);

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

        // Centralized formatting: percent/flat rule comes from the stat (Health is always flat),
        // sign + color (green positive / red negative) are handled by StatsFormatUtils.
        string formattedValue;
        if (mod.CharacterStat != ECharacterStat.None)
            formattedValue = StatsFormatUtils.FormatModifier(mod.CharacterStat, mod.Value);
        else if (mod.SpellStat != ESpellStat.None)
            formattedValue = StatsFormatUtils.FormatSpellModifier(mod.SpellStat, mod.Value);
        else
            formattedValue = StatsFormatUtils.FormatValue(mod.Value, isPercentage: false);

        return $"{formattedValue} {StatsFormatUtils.Humanize(target)}";
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
        var lockedView = ResolveLockedView();
        if (lockedView != null)
            lockedView.SetActive(false);
        if (InfoContainer != null)
            InfoContainer.SetActive(false);
    }

    /// <summary>
    /// The panel to toggle for the locked state. Uses the explicit <see cref="LockedView"/>
    /// when assigned, otherwise falls back to the cost container's parent (the cost container
    /// is nested inside the locked-view panel), so visibility works even if LockedView is unset.
    /// </summary>
    private GameObject ResolveLockedView()
    {
        if (LockedView != null)
            return LockedView;

        if (CostContainer != null && CostContainer.transform.parent != null)
            return CostContainer.transform.parent.gameObject;

        return CostContainer;
    }
}
