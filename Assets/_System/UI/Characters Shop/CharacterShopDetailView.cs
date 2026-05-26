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
    public GameObject CostContainer;
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

    public void PlayUnlockVfx()
    {
        if (UnlockVfxPrefab == null || CharacterPreviewContainer == null)
            return;

        var vfx = Instantiate(UnlockVfxPrefab, CharacterPreviewContainer);
        vfx.transform.localPosition = VfxLocalOffset;
        vfx.transform.localRotation = Quaternion.identity;
        vfx.transform.localScale = AnimScale;
        SetLayerRecursive(vfx, CharacterPreviewContainer.gameObject.layer);

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

    public void Refresh(CharacterSO data, bool isUnlocked)
    {
        Clear();

        if (data == null || CharacterPreviewContainer == null)
            return;

        if (!isUnlocked)
        {
            ShowLocked(data);
            return;
        }

        ShowUnlocked(data);
    }

    private void ShowLocked(CharacterSO data)
    {
        // Silhouette preview
        if (DefaultCharacter != null && CharacterPreviewContainer != null)
            Instantiate(DefaultCharacter, CharacterPreviewContainer);

        if (CharacterNameText != null)
            CharacterNameText.text = DefaultName;

        // Cost container visible, info hidden
        if (CostContainer != null)
            CostContainer.SetActive(true);
        if (InfoContainer != null)
            InfoContainer.SetActive(false);

        ShowCost(data.PurchaseCost);
    }

    private void ShowUnlocked(CharacterSO data)
    {
        // Real preview
        Tween.Delay(
            duration: SpawnDelay,
            () =>
            {
                if (data.UIPrefab != null)
                    Instantiate(data.UIPrefab, CharacterPreviewContainer);
            }
        );

        // if (data.UIPrefab != null && CharacterPreviewContainer != null)
        //     Instantiate(data.UIPrefab, CharacterPreviewContainer);

        if (CharacterNameText != null)
            CharacterNameText.text = data.DisplayName.ToUpper();

        // Info container visible, cost hidden
        if (InfoContainer != null)
            InfoContainer.SetActive(true);
        if (CostContainer != null)
            CostContainer.SetActive(false);

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
            instance.Refresh(cost.Type, resource != null ? resource.Icon : null, cost.Amount);
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

            object rawValue = field.GetValue(coreStats);

            // Skip stats at their neutral/default value and only show what differentiates this character
            if (IsDefaultValue(field, attr, rawValue))
                continue;

            // For float fields, adjust value relative to neutral (delta from neutral)
            object displayValue = rawValue;
            if (field.FieldType == typeof(float))
                displayValue = ((float)rawValue) - attr.NeutralValue;

            string formattedValue = string.Format(attr.Format, displayValue);
            CreateStatRow(attr.DisplayName, formattedValue);
        }
    }

    private static bool IsDefaultValue(FieldInfo field, UIStatAttribute attr, object rawValue)
    {
        // MaxHealth always shown
        if (field.Name == nameof(CoreStats.MaxHealth))
            return false;

        if (field.FieldType == typeof(float))
        {
            float val = (float)rawValue;
            return Mathf.Approximately(val, attr.NeutralValue);
        }

        if (field.FieldType == typeof(int))
            return (int)rawValue == 0;

        return false;
    }

    private void CreateStatRow(string name, string value)
    {
        var row = Instantiate(CharacterShopStatItemPrefab, CharacterStatsContainer);
        row.Init(name, value);
    }

    public void Clear()
    {
        // Clear preview
        if (CharacterPreviewContainer != null)
        {
            foreach (Transform child in CharacterPreviewContainer)
                Destroy(child.gameObject);
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
        if (CostContainer != null)
            CostContainer.SetActive(false);
        if (InfoContainer != null)
            InfoContainer.SetActive(false);
    }
}
