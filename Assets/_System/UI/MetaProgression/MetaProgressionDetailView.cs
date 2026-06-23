using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using _System.Settings;

/// <summary>
/// Detail panel for the meta-progression shop.
/// Shows selected upgrade info: current level, next bonus, cost, description.
/// </summary>
public class MetaProgressionDetailView : MonoBehaviour
{
    [Header("Upgrade Info")]
    [SerializeField] private TMP_Text _upgradeNameText;
    [SerializeField] private TMP_Text _descriptionText;
    [SerializeField] private Image _iconImage;

    [Header("Level Display")]
    [SerializeField] private TMP_Text _currentLevelText;
    [SerializeField] private TMP_Text _currentBonusText;
    [SerializeField] private TMP_Text _nextBonusText;

    [Header("Cost")]
    [SerializeField] private GameObject _costContainer;
    [SerializeField] private ResourceWidgetItem _costItemPrefab;
    [SerializeField] private ResourceDatabaseSO _resourceDatabase;

    [Header("Purchase Button")]
    [SerializeField] private Button _purchaseButton;
    [SerializeField] private TMP_Text _purchaseButtonText;

    [Tooltip("Scale applied to the purchase button while it is focused (controller feedback).")]
    [SerializeField] private float _purchaseFocusScale = 1.1f;

    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private Material _purchaseOutlineMat;

    [Header("Maxed Out")]
    [SerializeField] private GameObject _maxedContainer;
    [SerializeField] private TMP_Text _maxedText;

    private MetaProgressionController _controller;
    private MetaUpgradeSO _currentData;
    private int _currentLevel;

    private void Awake()
    {
        SetVisible(false);
    }

    public void SetController(MetaProgressionController controller)
    {
        _controller = controller;

        if (_purchaseButton != null)
        {
            _purchaseButton.onClick.RemoveAllListeners();
            _purchaseButton.onClick.AddListener(controller.PurchaseUpgrade);
        }
    }

    public void Refresh(MetaUpgradeSO data, int level, int index, ResourceDatabaseSO resourceDb)
    {
        _currentData = data;
        _currentLevel = level;
        _resourceDatabase = resourceDb;

        if (data == null)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        // Basic info
        if (_upgradeNameText != null) _upgradeNameText.text = data.DisplayName ?? data.TargetStat.ToString();
        if (_descriptionText != null) _descriptionText.text = data.Description;
        if (_iconImage != null) _iconImage.sprite = data.Icon;

        int maxLevel = data.BonusPerLevel != null ? data.BonusPerLevel.Length : 5;
        bool isMaxed = level >= maxLevel;

        string previewHex = CpUISettings.PipPreviewColorHex;

        // Level: current, with a preview of the next level after purchase ("Niveau 3 → 4 / 5").
        if (_currentLevelText != null)
        {
            _currentLevelText.text = isMaxed
                ? $"MAXED"
                : $"lvl. {level} <color={previewHex}>→ {level + 1}</color>";
        }

        // Bonus: current total (colored by sign) with the after-purchase total previewed next to it.
        // Self-contained in _currentBonusText so a single wired label is enough; _nextBonusText is optional.
        if (_currentBonusText != null)
        {
            string current = FormatBonus(data.GetTotalBonus(level), data.TargetStat);
            _currentBonusText.text = isMaxed
                ? current
                : $"{current} <color={previewHex}>→ {FormatBonus(data.GetTotalBonus(level + 1), data.TargetStat, colorize: false)}</color>";
        }

        // Optional standalone "after purchase" label (preview color). Skipped when not assigned.
        if (_nextBonusText != null)
        {
            _nextBonusText.text = isMaxed
                ? "MAX"
                : $"<color={previewHex}>→ {FormatBonus(data.GetTotalBonus(level + 1), data.TargetStat, colorize: false)}</color>";
        }

        // Cost / maxed display
        if (_maxedContainer != null) _maxedContainer.SetActive(isMaxed);
        if (_costContainer != null) _costContainer.SetActive(!isMaxed);
        if (_purchaseButton != null) _purchaseButton.gameObject.SetActive(!isMaxed);

        if (!isMaxed && _purchaseButtonText != null)
            _purchaseButtonText.text = "Upgrade";

        // Show cost
        ShowCost(data, level);
    }

    private void ShowCost(MetaUpgradeSO data, int level)
    {
        // Clear old cost widgets
        if (_costContainer != null)
        {
            foreach (Transform child in _costContainer.transform)
                Destroy(child.gameObject);
        }

        if (_costItemPrefab == null || _resourceDatabase == null || _costContainer == null)
            return;

        var cost = data.GetCostForLevel(level); // cost at current level index
        // if (cost.Amount <= 0)
        //     return;

        var resource = _resourceDatabase.GetResource(cost.Type);
        var instance = Instantiate(_costItemPrefab, _costContainer.transform);
        instance.Refresh(cost.Type, resource != null ? resource.Icon : null,
            resource != null ? resource.Color : Color.white, cost.Amount);
    }

    private void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    private static string FormatBonus(float value, ECharacterStat stat, bool colorize = true)
        => StatsFormatUtils.FormatModifier(stat, value, colorize);

    /// <summary>
    /// Highlights the purchase button when an upgrade is committed via controller (so the player
    /// sees what the next Interact will buy): a subtle scale-pop plus a colored outline driven on
    /// the button image's UI outline shader (_OutlineColor = CpUISettings.ItemOutlineSelected while
    /// committed, transparent otherwise). No-op while the button is hidden (maxed upgrade).
    /// </summary>
    public void SetPurchaseFocused(bool focused)
    {
        if (_purchaseButton == null)
            return;

        bool canShow = focused && _purchaseButton.gameObject.activeSelf;

        // Material cloned on first use so we don't repaint other UI graphics sharing the same outline
        // asset (Back button, grid items).
        if (_purchaseButton.image != null && _purchaseButton.image.material != null)
        {
            if (_purchaseOutlineMat == null)
            {
                _purchaseOutlineMat = new Material(_purchaseButton.image.material);
                _purchaseButton.image.material = _purchaseOutlineMat;
            }

            Color outlineColor = CpUISettings.ItemOutlineSelected;
            if (!canShow)
                outlineColor.a = 0f;
            _purchaseOutlineMat.SetColor(OutlineColorId, outlineColor);
        }

        _purchaseButton.transform.localScale =
            canShow ? Vector3.one * _purchaseFocusScale : Vector3.one;
    }

    public void Clear()
    {
        if (_costContainer != null)
        {
            foreach (Transform child in _costContainer.transform)
                Destroy(child.gameObject);
        }

        SetVisible(false);
    }
}
