using System;
using System.Reflection;
using TMPro;
using UnityEngine;

/// <summary>
/// Represents the selected character prview container
/// </summary>
public class CharacterShopDetailView : ShopDetailViewBase<CharacterSO>
{
    [Header("Preview")] public TMP_Text CharacterNameText;
    public Transform CharacterPreviewContainer;
    public TMP_Text CharacterDescriptionText;

    [Header("Stats")] public TMP_Text CharacterStatsText;
    public Transform CharacterStatsContainer;
    public CharacterShopStatItemComponent characterShopStatStatPrefab;

    private CharacterSO _data;

    public void Refresh(CharacterSO data, bool isUnlocked)
    {
        if (data == null)
            return;

        _data = data;

        if (data.UIPrefab != null && CharacterPreviewContainer != null)
        {
            foreach (Transform child in CharacterPreviewContainer.transform)
                Destroy(child.gameObject);

            Instantiate(data.UIPrefab, CharacterPreviewContainer.transform);
        }

        if (CharacterNameText != null)
            CharacterNameText.text = data.DisplayName.ToUpper();

        if (CharacterDescriptionText != null)
            CharacterDescriptionText.text = data.Description;

        RefreshStats(_data.coreStats);
    }


    public void RefreshStats(CoreStats coreStats)
    {
        foreach (Transform child in CharacterStatsContainer)
            Destroy(child.gameObject);

        Type type = typeof(CoreStats);
        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);

        foreach (FieldInfo field in fields)
        {
            var attr = field.GetCustomAttribute<UIStatAttribute>();

            if (attr == null)
                continue;

            object rawValue = field.GetValue(coreStats);
            string displayValue = string.Format(attr.Format, rawValue);

            CreateStatRow(attr.DisplayName, displayValue);
        }
    }

    private void CreateStatRow(string name, string value)
    {
        var row = Instantiate(characterShopStatStatPrefab, CharacterStatsContainer);
        row.Init(name, value);
    }
}