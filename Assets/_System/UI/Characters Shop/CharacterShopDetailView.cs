using System;
using System.Reflection;
using PrimeTween;
using TMPro;
using UnityEngine;

/// <summary>
/// Represents the selected character preview container.
/// Locked: shows purchase cost. Unlocked: shows description + stats.
/// </summary>
public class CharacterShopDetailView : ShopDetailViewBase<CharacterSO>
{
    [Header("Preview")]
    public Transform CharacterPreviewContainer;
    public GameObject DefaultCharacter;
    public TMP_Text CharacterNameText;

    [Header("Overlapped Detail Containers")]
    [Tooltip(
        "Locked-state panel, toggled on when the character is locked. Assign the 'Locked View' object (the parent of the Cost Container), NOT the inner cost container."
    )]
    public GameObject LockedView;

    [Tooltip(
        "Container with the layout group where cost item widgets are spawned (inside Locked View)."
    )]
    public GameObject CostContainer;

    [Tooltip("Unlocked-state panel, toggled on when the character is unlocked.")]
    public GameObject InfoContainer;

    [Header("Cost Display (locked)")]
    public ResourceWidgetItem CostItemPrefab;
    public ResourceDatabaseSO ResourceDatabase;

    [Header("Description Display (unlocked)")]
    public TMP_Text CharacterDescriptionText;

    public Transform CharacterStatsContainer;
    public CharacterShopStatItemComponent CharacterShopStatItemPrefab;

    [Header("Default Strings")]
    public string DefaultName = "???";

    [Header("NFC Purchase VFX")]
    public GameObject UnlockVfxPrefab;
    public Vector3 VfxLocalOffset = new Vector3(0f, 0f, 1.5f);
    public float VfxLifetime = 3f;
    public float SpawnDelay = 1.05f;
    public Vector3 AnimScale = new Vector3(1f, 1f, 1f);

    // Tracked so the reveal survives a preview rebuild (Clear preserves it) and is cleaned up when the
    // shop closes — mirroring the amulet shop, whose VFX is immune to refreshes because it's unparented.
    private GameObject _unlockVfx;

    // Delayed purchase reveal: shows the locked/base model first, then swaps in the real character
    // after SpawnDelay so the swap lands in time with the unlock VFX.
    private Tween _unlockRevealTween;

    public void PlayUnlockVfx()
    {
        if (UnlockVfxPrefab == null || CharacterPreviewContainer == null)
            return;

        // Replace any still-playing previous reveal.
        if (_unlockVfx != null)
            Destroy(_unlockVfx);

        _unlockVfx = Instantiate(UnlockVfxPrefab, CharacterPreviewContainer);
        _unlockVfx.transform.localPosition = VfxLocalOffset;
        _unlockVfx.transform.localRotation = Quaternion.identity;
        _unlockVfx.transform.localScale = AnimScale;
        SetLayerRecursive(_unlockVfx, CharacterPreviewContainer.gameObject.layer);

        _unlockVfx.GetComponent<ParticleSystem>().Play();

        if (VfxLifetime > 0f)
            Destroy(_unlockVfx, VfxLifetime);
    }

    private void OnDisable()
    {
        if (_unlockRevealTween.isAlive)
            _unlockRevealTween.Stop();

        if (_unlockVfx != null)
        {
            Destroy(_unlockVfx);
            _unlockVfx = null;
        }
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    /// <summary>
    /// Refreshes the detail view. <paramref name="animateUnlock"/> should only be true when the
    /// character was just purchased: it plays the delayed reveal (locked/base model shown first, then
    /// the real character after <see cref="SpawnDelay"/>). On plain selection switches it must be false
    /// so the character appears immediately with no base-model reshow.
    /// </summary>
    public void Refresh(CharacterSO data, bool isUnlocked, bool animateUnlock = false)
    {
        Clear();

        if (data == null || CharacterPreviewContainer == null)
            return;

        if (!isUnlocked)
        {
            ShowLocked(data);
            return;
        }

        ShowUnlocked(data, animateUnlock);
    }

    private void ShowLocked(CharacterSO data)
    {
        // Preview
        if (DefaultCharacter != null && CharacterPreviewContainer != null)
        {
            var previewObj = Instantiate(DefaultCharacter, CharacterPreviewContainer);
            previewObj.transform.position = CharacterPreviewContainer.position;
        }

        if (CharacterNameText != null)
            CharacterNameText.text = DefaultName;

        // Locked panel visible, info hidden
        var lockedView = ResolveLockedView();
        if (lockedView != null)
            lockedView.SetActive(true);
        if (InfoContainer != null)
            InfoContainer.SetActive(false);

        ShowCost(data.PurchaseCost);
    }

    private void ShowUnlocked(CharacterSO data, bool animateUnlock)
    {
        // Preview
        if (animateUnlock)
        {
            // Purchase reveal: show the locked/base model first, then swap in the real character
            // after SpawnDelay (timed with the unlock VFX).
            if (DefaultCharacter != null && CharacterPreviewContainer != null)
            {
                var baseObj = Instantiate(DefaultCharacter, CharacterPreviewContainer);
                baseObj.transform.position = CharacterPreviewContainer.position;
            }

            _unlockRevealTween = Tween.Delay(
                duration: SpawnDelay,
                () =>
                {
                    if (CharacterPreviewContainer == null)
                        return;

                    // Destroy the base preview (keep the unlock VFX), then swap in the real character.
                    foreach (Transform child in CharacterPreviewContainer)
                    {
                        if (_unlockVfx != null && child.gameObject == _unlockVfx)
                            continue;
                        Destroy(child.gameObject);
                    }

                    if (data.UIPrefab != null)
                    {
                        var previewObj = Instantiate(data.UIPrefab, CharacterPreviewContainer);
                        previewObj.transform.position = CharacterPreviewContainer.position;
                    }
                }
            );
        }
        else
        {
            // Plain selection switch: show the character immediately, no base-model reshow.
            if (data.UIPrefab != null && CharacterPreviewContainer != null)
            {
                var previewObj = Instantiate(data.UIPrefab, CharacterPreviewContainer);
                previewObj.transform.position = CharacterPreviewContainer.position;
            }
        }

        if (CharacterNameText != null)
            CharacterNameText.text = data.DisplayName.ToUpper();

        // Info container visible, locked panel hidden
        if (InfoContainer != null)
            InfoContainer.SetActive(true);
        var lockedView = ResolveLockedView();
        if (lockedView != null)
            lockedView.SetActive(false);

        if (CharacterDescriptionText != null)
            CharacterDescriptionText.text = data.Description;

        RefreshStats(data.coreStats);
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
            instance.Refresh(cost.Type, resource != null ? resource.Icon : null,
                resource != null ? resource.Color : Color.white, cost.Amount);
        }
    }

    public void RefreshStats(CoreStats coreStats)
    {
        if (CharacterStatsContainer == null || CharacterShopStatItemPrefab == null)
            return;

        Type type = typeof(CoreStats);
        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);

        foreach (FieldInfo field in fields)
        {
            var attr = field.GetCustomAttribute<UIStatAttribute>();
            if (attr == null)
                continue;

            float rawValue = Convert.ToSingle(field.GetValue(coreStats));

            // Skip stats at their neutral/default value: only show what differentiates this character.
            if (IsDefaultValue(field, attr, rawValue))
                continue;

            string formattedValue = StatsFormatUtils.FormatPanelStat(
                rawValue,
                attr.Stat,
                attr.NeutralValue,
                attr.Absolute,
                attr.Suffix,
                attr.Decimals
            );
            CreateStatRow(attr.DisplayName, formattedValue);
        }
    }

    private static bool IsDefaultValue(FieldInfo field, UIStatAttribute attr, float rawValue)
    {
        // MaxHealth is always shown.
        if (field.Name == nameof(CoreStats.MaxHealth))
            return false;

        return Mathf.Approximately(rawValue, attr.NeutralValue);
    }

    private void CreateStatRow(string name, string value)
    {
        var row = Instantiate(CharacterShopStatItemPrefab, CharacterStatsContainer);
        row.Init(name, value);
    }

    public void Clear()
    {
        // Cancel a pending delayed reveal swap so a refresh can't instantiate a stale duplicate later.
        if (_unlockRevealTween.isAlive)
            _unlockRevealTween.Stop();

        // Clear preview (but keep an in-flight unlock VFX so a refresh doesn't cut the reveal short).
        if (CharacterPreviewContainer != null)
        {
            foreach (Transform child in CharacterPreviewContainer)
            {
                if (_unlockVfx != null && child.gameObject == _unlockVfx)
                    continue;
                Destroy(child.gameObject);
            }
        }

        // Destroy cost items
        if (CostContainer != null)
        {
            foreach (Transform child in CostContainer.transform)
                Destroy(child.gameObject);
        }

        // Destroy stat rows
        if (CharacterStatsContainer != null)
        {
            foreach (Transform child in CharacterStatsContainer)
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
