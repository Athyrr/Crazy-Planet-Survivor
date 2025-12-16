using System.Reflection;
using UnityEngine.UI;
using UnityEngine;

/// <summary>
/// Represents the section that display the selected character base stats.
/// </summary>
public class CharacterStatsViewComponent : MonoBehaviour
{
    [Header("Stats")]

    public CharacterStatsItemComponent StatItemPrefab;

    public Transform StatsItemsContainer;


    [Header("Spells")]

    public Image SpellIcon;

    public void Refresh(BaseStats stats)
    {
        foreach (Transform child in StatsItemsContainer)
            Destroy(child.gameObject);

        System.Type type = typeof(BaseStats);

        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);

        foreach (FieldInfo field in fields)
        {
            var attr = field.GetCustomAttribute<UIStatAttribute>();

            if (attr == null) 
                continue;

            object rawValue = field.GetValue(stats);
            string displayValue = string.Format(attr.Format, rawValue);

            CreateStatRow(attr.DisplayName, displayValue);
        }
    }

    private void CreateStatRow(string name, string value)
    {
        var row = Instantiate(StatItemPrefab, StatsItemsContainer);
        row.Init(name, value);
    }
}
