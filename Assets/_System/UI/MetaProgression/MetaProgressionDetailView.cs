using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("Pips")]
    [SerializeField] private Image[] _detailPips;   // 5 pips in detail view
    [SerializeField] private Sprite _pipFilled;
    [SerializeField] private Sprite _pipEmpty;

    [Header("Cost")]
    [SerializeField] private GameObject _costContainer;
    [SerializeField] private ResourceWidgetItem _costItemPrefab;
    [SerializeField] private ResourceDatabaseSO _resourceDatabase;

    [Header("Purchase Button")]
    [SerializeField] private Button _purchaseButton;
    [SerializeField] private TMP_Text _purchaseButtonText;

    [Header("Maxed Out")]
    [SerializeField] private GameObject _maxedContainer;
    [SerializeField] private TMP_Text _maxedText;

    [Header("Colors")]
    [SerializeField] private Color _normalPipColor = Color.white;
    [SerializeField] private Color _filledPipColor = Color.white;
    [SerializeField] private Color _maxedPipColor = new Color(0.2f, 1f, 0.2f);

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

        // Level
        if (_currentLevelText != null)
            _currentLevelText.text = $"lvl {level} / {maxLevel}";

        // Current total bonus
        if (_currentBonusText != null)
        {
            float totalBonus = data.GetTotalBonus(level);
            _currentBonusText.text = FormatBonus(totalBonus, data.TargetStat);
        }

        // Next level bonus
        if (_nextBonusText != null)
        {
            if (isMaxed)
            {
                _nextBonusText.text = "MAX";
            }
            else
            {
                float nextBonus = data.GetLevelBonus(level + 1);
                _nextBonusText.text = $"Prochain: {FormatBonus(nextBonus, data.TargetStat)}";
            }
        }

        // Detail pips
        RefreshDetailPips(level, maxLevel, isMaxed);

        // Cost / maxed display
        if (_maxedContainer != null) _maxedContainer.SetActive(isMaxed);
        if (_costContainer != null) _costContainer.SetActive(!isMaxed);
        if (_purchaseButton != null) _purchaseButton.gameObject.SetActive(!isMaxed);

        if (!isMaxed && _purchaseButtonText != null)
            _purchaseButtonText.text = "AMÉLIORER";

        // Show cost
        ShowCost(data, level);
    }

    private void RefreshDetailPips(int level, int maxLevel, bool isMaxed)
    {
        if (_detailPips == null) return;

        for (int i = 0; i < _detailPips.Length; i++)
        {
            if (_detailPips[i] == null) continue;

            bool isFilled = i < level;
            _detailPips[i].sprite = isFilled ? _pipFilled : _pipEmpty;
            _detailPips[i].color = isMaxed && isFilled ? _maxedPipColor : _filledPipColor;
        }
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
        if (cost.Amount <= 0)
            return;

        var resource = _resourceDatabase.GetResource(cost.Type);
        var instance = Instantiate(_costItemPrefab, _costContainer.transform);
        instance.Refresh(cost.Type, resource != null ? resource.Icon : null, cost.Amount);
    }

    private void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    private static string FormatBonus(float value, ECharacterStat stat)
    {
        bool isFlat = stat is ECharacterStat.PierceCount or ECharacterStat.BounceCount;
        if (isFlat)
            return $"{(int)value:+0;-0;0}";
        else
            return $"{value:+0%;-0%;0}";
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
