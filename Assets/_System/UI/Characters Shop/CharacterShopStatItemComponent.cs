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
        LabelText.text = FormatName(name);

        // Set color
        if (value.StartsWith("+"))
            ValueText.text = $"<color=#4ADE80>{value}</color>"; // green bonus
        else if (value.StartsWith("-"))
            ValueText.text = $"<color=#F87171>{value}</color>"; // red malus
        else
            ValueText.text = value;
    }

    /// <summary>
    /// Format name display. MaxHealth -> Max Health
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    private string FormatName(string name)
    {
        return System.Text.RegularExpressions.Regex.Replace(name, "(\\B[A-Z])", " $1");
    }
}
