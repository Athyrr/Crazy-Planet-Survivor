using System;
using System.Collections.Generic;
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

    [Header("Tags")]
    [Tooltip("Parent (HorizontalLayoutGroup) the tag chips are spawned into. Spell cards only.")]
    public Transform TagsContainer;
    [Tooltip("Chip prefab spawned once per spell tag (e.g. PF_SpellTag). Its child TextMeshPro is set to the tag name.")]
    public GameObject TagChipPrefab;

    [Header("Spell Level (optional)")]
    [Tooltip("Optional dedicated label for the spell level (e.g. 'Lv 1 → 2'). " +
             "If left empty, the level is appended to the Upgrade Type text instead.")]
    public TextMeshPro SpellLevelText;

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

    // Reused scratch buffer so spawning tag chips does not allocate every level-up.
    private static readonly List<string> _tagNameBuffer = new List<string>();


    public void SetData(ref UpgradeBlob upgradeData, int dbIndex, in UpgradeDisplayContext context)
    {
        _dbIndex = dbIndex;

        if (TitleText)
            TitleText.text = GetTitle(ref upgradeData);

        if (DescriptionText)
            DescriptionText.text = upgradeData.Description.ToString();

        RefreshUpgradeType(ref upgradeData, in context);
        RefreshTags(ref upgradeData, in context);
        RefreshStatsDetails(ref upgradeData, in context);
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

    private void RefreshUpgradeType(ref UpgradeBlob data, in UpgradeDisplayContext context)
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

        string level = BuildSpellLevelText(ref data, in context);

        if (SpellLevelText)
        {
            // Dedicated label: type and level stay separate.
            UpgradeTypeText.text = text;
            SpellLevelText.text = level;
            SpellLevelText.color = CpUISettings.PipPreviewColor;
        }
        else if (!string.IsNullOrEmpty(level))
        {
            // No dedicated label: fold the level into the type line.
            UpgradeTypeText.text = $"{text}   {level}";
        }
        else
        {
            UpgradeTypeText.text = text;
        }
    }

    /// <summary>
    /// Builds the spell level preview: "Lv 1" for an unlock (the level it will be once owned),
    /// "Lv X → X+1" for an upgrade of an owned spell. Empty for stat / tagged upgrades.
    /// </summary>
    private static string BuildSpellLevelText(ref UpgradeBlob data, in UpgradeDisplayContext context)
    {
        switch (data.UpgradeType)
        {
            case EUpgradeType.UnlockSpell:
                return "Lv 1";

            case EUpgradeType.UpgradeSpell:
                if (data.SpellID != ESpellID.None && context.TryGetActiveSpell(data.SpellID, out var active))
                    return $"Lv {active.Level} → {active.Level + 1}";
                return string.Empty;

            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Spawns one chip per spell tag into <see cref="TagsContainer"/>. Tags are shown on spell cards
    /// only: a spell's authored tags for unlock / specific upgrades, the targeted tags for a tagged
    /// upgrade. Stat-upgrade cards have no tags and the container is hidden.
    /// </summary>
    private void RefreshTags(ref UpgradeBlob data, in UpgradeDisplayContext context)
    {
        if (!TagsContainer)
            return;

        // Clear any previous chips (also removes the design-time mockup chips on first build).
        for (int i = TagsContainer.childCount - 1; i >= 0; i--)
            Destroy(TagsContainer.GetChild(i).gameObject);

        ESpellTag tags = ResolveDisplayTags(ref data, in context);
        bool showTags = data.UpgradeType != EUpgradeType.PlayerStat
                        && TagChipPrefab != null
                        && SpellTagUtils.HasAnyTag(tags);

        TagsContainer.gameObject.SetActive(showTags);
        if (!showTags)
            return;

        SpellTagUtils.GetTagNames(tags, _tagNameBuffer);
        for (int i = 0; i < _tagNameBuffer.Count; i++)
        {
            var chip = Instantiate(TagChipPrefab, TagsContainer);
            var label = chip.GetComponentInChildren<TextMeshPro>();
            if (label != null)
            {
                label.text = _tagNameBuffer[i];
                label.color = CpUISettings.ComplementaryColor;
            }
        }
    }

    /// <summary>Resolves which tags a spell card should display.</summary>
    private static ESpellTag ResolveDisplayTags(ref UpgradeBlob data, in UpgradeDisplayContext context)
    {
        if (data.UpgradeType == EUpgradeType.PlayerStat)
            return ESpellTag.None;

        // Tagged upgrade (no specific spell): show the targeted tags themselves.
        if (data.SpellID == ESpellID.None)
            return data.SpellTags;

        // Unlock / specific-spell upgrade: the spell's own authored tags, plus any tag this upgrade adds.
        ESpellTag tags = data.SpellTags;
        if (context.TryGetSpellTags(data.SpellID, out var ownTags))
            tags |= ownTags;

        return tags;
    }

    private void RefreshStatsDetails(ref UpgradeBlob data, in UpgradeDisplayContext context)
    {
        switch (data.UpgradeType)
        {
            case EUpgradeType.PlayerStat:
                BuildModifierLines(ref data, in context, out string labels, out string values);

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
                BuildSpellUpgradeLine(ref data, in context, out string spellLabel, out string spellValue);

                if (StatLabelText)
                    StatLabelText.text = spellLabel;

                if (StatValueText)
                    StatValueText.text = spellValue;

                break;
        }
    }

    /// <summary>
    /// Builds the label/value text (one line per modifier) for a multi-modifier stat upgrade.
    /// When the player's current stats are known, the value shows a "before → after" preview;
    /// otherwise it falls back to the raw modifier delta.
    /// </summary>
    private static void BuildModifierLines(ref UpgradeBlob data, in UpgradeDisplayContext context,
        out string labels, out string values)
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

            if (context.HasPlayerStats && TryGetCurrentStat(in context.PlayerStats, mod.CharacterStat, out float before))
                valueBuilder.Append(StatsFormatUtils.FormatStatBeforeAfter(mod.CharacterStat, before, before + mod.Value));
            else
                valueBuilder.Append(StatsFormatUtils.FormatModifier(mod.CharacterStat, mod.Value));
        }

        labels = labelBuilder.ToString();
        values = valueBuilder.ToString();
    }

    /// <summary>
    /// Builds the label/value line for a spell upgrade. For an owned specific spell the value shows a
    /// "before → after" preview of the affected spell stat; otherwise it shows the raw modifier.
    /// </summary>
    private static void BuildSpellUpgradeLine(ref UpgradeBlob data, in UpgradeDisplayContext context,
        out string label, out string value)
    {
        string targetName = data.SpellID != ESpellID.None
            ? StatsFormatUtils.Humanize(data.SpellID.ToString())
            : data.SpellTags.ToString();

        label = $"{targetName} {StatsFormatUtils.Humanize(data.SpellStat.ToString())}";

        if (data.SpellID != ESpellID.None && context.TryGetActiveSpell(data.SpellID, out var active))
        {
            float before = GetCurrentSpellStat(in active, data.SpellStat);
            value = StatsFormatUtils.FormatSpellStatBeforeAfter(data.SpellStat, before, before + data.Value);
        }
        else
        {
            value = StatsFormatUtils.FormatSpellModifier(data.SpellStat, data.Value);
        }
    }

    /// <summary>Reads the player's current accumulated value for <paramref name="stat"/>.</summary>
    private static bool TryGetCurrentStat(in CoreStats stats, ECharacterStat stat, out float value)
    {
        switch (stat)
        {
            case ECharacterStat.MaxHealth: value = stats.MaxHealth; return true;
            case ECharacterStat.HealthRegen: value = stats.HealthRegen; return true;
            case ECharacterStat.Armor: value = stats.Armor; return true;
            case ECharacterStat.Speed: value = stats.MoveSpeed; return true;
            case ECharacterStat.CollectRange: value = stats.PickupRange; return true;
            case ECharacterStat.Damage: value = stats.Damage; return true;
            case ECharacterStat.AttackSpeed: value = stats.AttackSpeed; return true;
            case ECharacterStat.SizeMultiplier: value = stats.SpellSize; return true;
            case ECharacterStat.SpellSpeed: value = stats.SpellSpeed; return true;
            case ECharacterStat.SpellDuration: value = stats.SpellDuration; return true;
            case ECharacterStat.CastRange: value = stats.CastRange; return true;
            case ECharacterStat.Amount: value = stats.Amount; return true;
            case ECharacterStat.PierceCount: value = stats.Pierce; return true;
            case ECharacterStat.BounceCount: value = stats.Bounce; return true;
            case ECharacterStat.CritChance: value = stats.CritChance; return true;
            case ECharacterStat.CritDamage: value = stats.CritDamage; return true;
            case ECharacterStat.Luck: value = stats.Luck; return true;
            default:
                // Health / status stats (Burn, Slow, Stun…) are not tracked on CoreStats.
                value = 0f;
                return false;
        }
    }

    /// <summary>Reads the active spell's current local bonus for <paramref name="stat"/>.</summary>
    private static float GetCurrentSpellStat(in ActiveSpell spell, ESpellStat stat)
    {
        switch (stat)
        {
            case ESpellStat.Damage: return spell.LocalDamageBonusMultiplier;
            case ESpellStat.CooldownReduction: return spell.LocalCooldownReducBonusMultiplier;
            case ESpellStat.Speed: return spell.LocalSpeedBonusMultiplier;
            case ESpellStat.Range: return spell.LocalRangeBonusMultiplier;
            case ESpellStat.Size: return spell.LocalSizeBonusMultiplier;
            case ESpellStat.Duration: return spell.LocalSpellDurationBonusMultiplier;
            case ESpellStat.Amount: return spell.LocalAmountBonus;
            case ESpellStat.TickRate: return spell.LocalTickRateBonusMultiplier;
            case ESpellStat.BounceCount: return spell.LocalBounceBonus;
            case ESpellStat.PierceCount: return spell.LocalPierceBonus;
            case ESpellStat.CritChance: return spell.LocalCritChanceBonusPercent;
            case ESpellStat.CritDamage: return spell.LocalCritDamageBonus;
            default: return 0f;
        }
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
