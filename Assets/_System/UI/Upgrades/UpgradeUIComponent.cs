using UnityEngine;
using TMPro; 

public class UpgradeUIComponent : MonoBehaviour
{
    [Header("Data")]
    public int DbIndex { get; private set; }

    [Header("3D Visuals")]
    public Transform VisualRoot; 
    public TextMeshPro LabelText;     
    public TextMeshPro DescriptionText;
    public TextMeshPro DetailsText;
    public SpriteRenderer IconRenderer;

    [Header("Hover Feedback")]
    public float HoverScale = 1.15f;
    public float SmoothSpeed = 15f;

    private bool _isHovered;

    private void Awake()
    {
        if (VisualRoot == null) 
            VisualRoot = transform;
    }

    public void SetData(ref UpgradeBlob upgradeData, int dbIndex)
    {
        DbIndex = dbIndex;

        if (LabelText) LabelText.text = GetTitleFromType(upgradeData.UpgradeType);

        if (DetailsText) DetailsText.text = FormatDetails(ref upgradeData);
    }

    public void SetHovered(bool isHovered)
    {
        if (_isHovered == isHovered)
            return;

        _isHovered = isHovered;

        if (LabelText)
            LabelText.color = isHovered ? Color.yellow : Color.white;
    }



    private string GetTitleFromType(EUpgradeType type)
    {
        return type == EUpgradeType.Stat ? "Stat Upgrade" : "New Spell";
    }

    private string FormatDetails(ref UpgradeBlob data)
    {
        if (data.UpgradeType == EUpgradeType.Stat)
        {
            string val = data.ModifierStrategy == EStatModiferStrategy.Flat
                ? $"+{data.Value}"
                : $"+{(data.Value * 100 - 100):F0}%";
            return $"{data.CharacterStat}\n<color=green>{val}</color>";
        }
        return $"{data.SpellID}";
    }
}