using UnityEngine;
using TMPro;

/// <summary>
/// Represents a single stat row in the end-of-run summary view.
/// </summary>
public class SummaryStat : MonoBehaviour
{
    public TMP_Text LabelText;
    public TMP_Text ValueText;

    public void Init(string label, string value)
    {
        LabelText.text = label;
        ValueText.text = value;
    }
}
