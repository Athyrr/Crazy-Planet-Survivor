using System;
using System.Text;
using _System.Settings;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using Vector3 = UnityEngine.Vector3;

/// <summary>
/// Represents an upgrade card displayed when the player is leveling up.
/// </summary>
public class UpgradeViewItem : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public event Action<UpgradeViewItem> PointerEntered;
    public event Action<UpgradeViewItem> PointerExited;
    public event Action<UpgradeViewItem> PointerClicked;

    void IPointerEnterHandler.OnPointerEnter(PointerEventData _) => PointerEntered?.Invoke(this);
    void IPointerExitHandler.OnPointerExit(PointerEventData _) => PointerExited?.Invoke(this);
    void IPointerClickHandler.OnPointerClick(PointerEventData _) => PointerClicked?.Invoke(this);

    [Header("3D Visuals")]
    public SpriteRenderer Icon;
    public TextMeshPro TitleText;
    public TextMeshPro DescriptionText;
    public TextMeshPro StatLabelText;
    public TextMeshPro StatValueText;
    public TextMeshPro UpgradeTypeText;

    [Header("Rarity / Crystal")]
    [Tooltip("Renderer of the upgrade crystal. Its material + density vary with rarity (or the spell material).")]
    public MeshRenderer CrystalRenderer;
    [Tooltip("Rarity label (e.g. RARE). Hidden / set to SPELL for spell cards.")]
    public TextMeshPro RarityText;

    [Header("Floating")] public float FloatingSpeed;
    public float FloatingAmplitude;

    private MaterialPropertyBlock _crystalPropertyBlock;

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
        RefreshCrystalAndRarity(ref upgradeData);
    }

    /// <summary>
    /// Applies the rarity crystal material + density (or the dedicated spell material) and the
    /// rarity label/color, all driven by <see cref="CpRaritySettings"/>.
    /// </summary>
    private void RefreshCrystalAndRarity(ref UpgradeBlob data)
    {
        bool isStat = data.UpgradeType == EUpgradeType.PlayerStat;

        Material crystalMaterial = isStat
            ? CpRaritySettings.GetCrystalMaterial(data.Rarity)
            : CpRaritySettings.SpellCrystalMaterial;
        float crystalDensity = isStat
            ? CpRaritySettings.GetCrystalDensity(data.Rarity)
            : CpRaritySettings.SpellCrystalDensity;

        if (CrystalRenderer != null)
        {
            if (crystalMaterial != null)
                CrystalRenderer.sharedMaterial = crystalMaterial;

            _crystalPropertyBlock ??= new MaterialPropertyBlock();
            CrystalRenderer.GetPropertyBlock(_crystalPropertyBlock);
            // The density only affects rendering when its toggle is enabled.
            _crystalPropertyBlock.SetFloat(CpRaritySettings.CrystalDensityEnableShaderProperty, 1f);
            _crystalPropertyBlock.SetFloat(CpRaritySettings.CrystalDensityShaderProperty, crystalDensity);
            CrystalRenderer.SetPropertyBlock(_crystalPropertyBlock);
        }

        if (RarityText != null)
        {
            RarityText.text = isStat ? CpRaritySettings.GetLabel(data.Rarity) : CpRaritySettings.SpellLabel;
            RarityText.color = isStat ? CpRaritySettings.GetTextColor(data.Rarity) : CpRaritySettings.SpellTextColor;
        }
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
                BuildModifierLines(ref data, out string labels, out string values);

                if (StatLabelText)
                    StatLabelText.text = labels;

                if (StatValueText)
                    StatValueText.text = values;

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
                    StatValueText.text = StatsFormatUtils.FormatSpellModifier(data.SpellStat, data.Value);

                break;
        }
    }

    /// <summary>Builds the label/value text (one line per modifier) for a multi-modifier stat upgrade.</summary>
    private static void BuildModifierLines(ref UpgradeBlob data, out string labels, out string values)
    {
        ref var modifiers = ref data.StatModifiers;
        if (modifiers.Length == 0)
        {
            labels = string.Empty;
            values = string.Empty;
            return;
        }

        var labelBuilder = new StringBuilder();
        var valueBuilder = new StringBuilder();
        for (int i = 0; i < modifiers.Length; i++)
        {
            if (i > 0)
            {
                labelBuilder.Append('\n');
                valueBuilder.Append('\n');
            }

            ref var mod = ref modifiers[i];
            labelBuilder.Append(StatsFormatUtils.Humanize(mod.CharacterStat.ToString()));
            valueBuilder.Append(StatsFormatUtils.FormatModifier(mod.CharacterStat, mod.Value));
        }

        labels = labelBuilder.ToString();
        values = valueBuilder.ToString();
    }

    private void RefreshColors(ref UpgradeBlob upgradeData)
    {
        if (!TitleText)
            return;

        switch (upgradeData.UpgradeType)
        {
            case EUpgradeType.PlayerStat:
                TitleText.color = CpUISettings.UpgradeStatTitleColor;
                break;
            case EUpgradeType.UnlockSpell:
                TitleText.color = CpUISettings.UpgradeUnlockTitleColor;
                break;
            case EUpgradeType.UpgradeSpell:
                TitleText.color = CpUISettings.UpgradeSpellTitleColor;
                break;
        }
    }

    private string GetTitle(ref UpgradeBlob upgradeData) => upgradeData.DisplayName.ToString();

    public void SetHovered(bool isHovered)
    {
        if (_isHovered == isHovered)
            return;

        _isHovered = isHovered;

        // Guard the field that is actually recolored (was guarding TitleText — a bug).
        if (StatLabelText)
            StatLabelText.color = isHovered ? CpUISettings.UpgradeCardHighlightColor : CpUISettings.UpgradeCardIdleColor;
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