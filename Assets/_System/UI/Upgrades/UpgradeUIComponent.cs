using UnityEngine;
using TMPro;
using System;

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

        if (LabelText)
            LabelText.text = GetTitle(ref upgradeData);

        if (DetailsText)
            DetailsText.text = FormatDetails(ref upgradeData);

        // Colors
        if (LabelText)
        {
            switch (upgradeData.UpgradeType)
            {
                case EUpgradeType.Stat:
                    LabelText.color = Color.white;
                    break;
                case EUpgradeType.UnlockSpell:
                    LabelText.color = Color.yellow;
                    break;
                case EUpgradeType.UpgradeSpell:
                    LabelText.color = Color.green;
                    break;
            }
        }
    }

    private string GetTitle(ref UpgradeBlob upgradeData) => upgradeData.DisplayName.ToString();

    public void SetHovered(bool isHovered)
    {
        if (_isHovered == isHovered)
            return;

        _isHovered = isHovered;

        if (LabelText)
            LabelText.color = isHovered ? Color.yellow : Color.white;
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