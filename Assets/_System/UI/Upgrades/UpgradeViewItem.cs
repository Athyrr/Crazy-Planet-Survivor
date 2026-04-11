using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using Vector3 = UnityEngine.Vector3;

/// <summary>
/// Represents an upgrade card displayed when the player is leveling up. 
/// </summary>
public class UpgradeViewItem : MonoBehaviour
{
    [Header("3D Visuals")]
    public SpriteRenderer Icon;
    public TextMeshPro TitleText;
    public TextMeshPro DescriptionText;
    public TextMeshPro StatLabelText;
    public TextMeshPro StatValueText;
    public TextMeshPro UpgradeTypeText;

    [Header("Floating")] public float FloatingSpeed;
    public float FloatingAmplitude;

    private bool _isHovered;
    private int _dbIndex;

    private Vector3 _intialPosition;
    private Vector3 _intialScale;

    public int DbIndex => _dbIndex;
    private Transform _visualRoot;


    public void SetData(ref UpgradeBlob upgradeData, int dbIndex)
    {
        _dbIndex = dbIndex;

        if (TitleText)
            TitleText.text = GetTitle(ref upgradeData);

        if (DescriptionText)
            DescriptionText.text = upgradeData.Description.ToString();

        RefreshUpgradeType(ref upgradeData);
        RefreshStatsDetails(ref upgradeData);
        RefreshColors(ref upgradeData);
    }

    private void RefreshUpgradeType(ref UpgradeBlob data)
    {
        if (!UpgradeTypeText)
            return;

        string text = string.Empty;
        switch (data.UpgradeType)
        {
            case EUpgradeType.PlayerStat:
                text = "Stat Upgrade";
                break;
            case EUpgradeType.UnlockSpell:
                text = "Spell Unlock";
                break;
            case EUpgradeType.UpgradeSpell:
                text = "Spell Upgrade";
                break;
        }

        UpgradeTypeText.text = text;
    }

    private void RefreshStatsDetails(ref UpgradeBlob data)
    {
        switch (data.UpgradeType)
        {
            case EUpgradeType.PlayerStat:
                if (StatLabelText)
                    StatLabelText.text = data.CharacterStat.ToString();

                if (StatValueText)
                {
                    string val = data.ModifierStrategy == EModiferStrategy.Flat
                        ? $"+{data.Value}"
                        : $"+{(data.Value * 100 - 100):F0}%";
                    StatValueText.text = $"<color=green>{val}</color>";
                }

                break;

            case EUpgradeType.UnlockSpell:
                if (StatLabelText)
                    StatLabelText.text = string.Empty;

                if (StatValueText)
                    StatValueText.text = string.Empty;
                break;

            case EUpgradeType.UpgradeSpell:
                string targetName = data.SpellID != ESpellID.None ? data.SpellID.ToString() : data.SpellTags.ToString();

                if (StatLabelText)
                    StatLabelText.text = $"{targetName} {data.SpellStat}";

                if (StatValueText)
                {
                    bool isPercentage = IsSpellStatPercentage(data.SpellStat);
                    StatValueText.text = $"<color=green>{FormatSpellValue(data.Value, isPercentage)}</color>";
                }

                break;
        }
    }

    private void RefreshColors(ref UpgradeBlob upgradeData)
    {
        if (!TitleText)
            return;

        switch (upgradeData.UpgradeType)
        {
            case EUpgradeType.PlayerStat:
                TitleText.color = Color.white;
                break;
            case EUpgradeType.UnlockSpell:
                TitleText.color = Color.yellow;
                break;
            case EUpgradeType.UpgradeSpell:
                TitleText.color = Color.green;
                break;
        }
    }

    private string GetTitle(ref UpgradeBlob upgradeData) => upgradeData.DisplayName.ToString();

    public void SetHovered(bool isHovered)
    {
        if (_isHovered == isHovered)
            return;

        _isHovered = isHovered;

        if (TitleText)
            StatLabelText.color = isHovered ? Color.yellow : Color.white;
    }

    private string FormatSpellValue(float value, bool isPercentage)
    {
        if (isPercentage)
        {
            float pct = (value - 1.0f) * 100f;
            return (pct > 0 ? "+" : "") + $"{pct:F0}%";
        }
        else
        {
            return (value > 0 ? "+" : "") + value.ToString();
        }
    }

    private bool IsSpellStatPercentage(ESpellStat stat)
    {
        switch (stat)
        {
            case ESpellStat.Amount:
            case ESpellStat.BounceCount:
            case ESpellStat.PierceCount:
                return false;
            default:
                return true;
        }
    }

    private void Update()
    {
        Vector3 targetPosition = _intialPosition;
        if (_isHovered)
        {
            float yOffset = Mathf.Sin(Time.time * FloatingSpeed) * FloatingAmplitude;
            targetPosition.y += yOffset;
        }
        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, Time.deltaTime * FloatingSpeed);
    }

    public void SetInitialPosition(Vector3 pos)
    {
        _intialPosition = pos;
        transform.localPosition = pos;
    }
}