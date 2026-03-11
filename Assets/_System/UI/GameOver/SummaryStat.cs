using TMPro;
using UnityEngine;

/// <summary>
/// UI element that represents a single stat in the summary screen after the game is over.
/// </summary>
public class SummaryStat : MonoBehaviour
{
    public TMP_Text StatLabelText;
    public TMP_Text StatValueText;
    
    public void Refresh(string label, string value)
    {
        if (StatLabelText != null) StatLabelText.text = label;
        if (StatValueText != null) StatValueText.text = value;
    }
}