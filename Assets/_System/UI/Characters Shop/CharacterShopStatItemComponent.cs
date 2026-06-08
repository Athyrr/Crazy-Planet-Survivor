using UnityEngine;
using TMPro;

/// <summary>
/// Represents stat row in UI. Displayed on character selection menu.
/// </summary>
public class CharacterShopStatItemComponent : StatTabViewItem
{
    public TMP_Text LabelText;
    public TMP_Text ValueText;

    public void Init(string name, string value)
    {
        LabelText.text = StatsFormatUtils.Humanize(name);
        ValueText.text = value; // already formatted + colored by StatsFormatUtils.FormatPanelStat
    }
}
