using UnityEngine;
using TMPro;

/// <summary>
/// Represents à stat row in UI. Displayed on character selection menu.
/// </summary>
public class CharacterStatsItemComponent : MonoBehaviour
{
    public TMP_Text LabelText;
    public TMP_Text ValueText;

    public void Init(string name, string value)
    {
        LabelText.text = FormatName(name);
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
