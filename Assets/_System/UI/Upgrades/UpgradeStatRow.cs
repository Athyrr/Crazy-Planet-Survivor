using TMPro;
using UnityEngine;

/// <summary>
/// One stat line on an upgrade card: a left-aligned label and a right-aligned value.
/// Spawned once per stat into the card's stats container by <see cref="UpgradeViewItem"/>.
/// </summary>
public class UpgradeStatRow : MonoBehaviour
{
    [Tooltip("Left label, e.g. 'Move Sp.'")]
    public TMP_Text Label;

    [Tooltip("Right value, e.g. '+10%' or '10 → 12' (rich-text colored by StatsFormatUtils).")]
    public TMP_Text Value;

    public void Set(string label, string value)
    {
        if (Label != null) Label.text = label;
        if (Value != null) Value.text = value;
    }
}
