using System;
using System.Reflection;
using UnityEngine.UI;
using UnityEngine;

/// <summary>
/// Represents the section that display the selected character base stats.
/// </summary>
public class CharacterStatsListView : ShopListViewBase<CharacterItemComponent>
{
    
    // todo make a single container for character details + stats
    
    
    [Header("Stats")] public CharacterItemComponent itemPrefab;

    public Transform StatsItemsContainer;
    
    [Header("Spells")] public Image SpellIcon;
    
    public void Refresh(CoreStats coreStats)
    {
        foreach (Transform child in StatsItemsContainer)
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
        var row = Instantiate(itemPrefab, StatsItemsContainer);
        row.Init(name, value);
    }
}