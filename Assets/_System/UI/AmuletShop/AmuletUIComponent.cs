using System;
using System.Linq;
using _System.ECS.Authorings.Ressources;
using _System.Settings;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class AmuletUIComponent : MonoBehaviour
{
    [SerializeField] private Button _amuletButton;
    [SerializeField] private TMP_Text _label;
    [SerializeField] private Image _icon;
    [SerializeField] private Image _border;
    [SerializeField] private EnumValues<ERessourceType, Sprite> _ressourcesSprite; // todo @hyverno move in icon database with CPBaseSettings
    [SerializeField] private UI_PlayerRessourceElementComponent _ressourceComponent; // same
    [SerializeField] private GameObject _ressourceComponentParent;
    
    private AmuletShopUIController _controller;
    private AmuletSO _amuletData;
    private int _databaseIndex;
    private bool _isUnlocked;
    private bool _isFocused;

    private static readonly int BackgroundColorShaderProperty = Shader.PropertyToID("_BackgroundColor");
    private static readonly int OutlineColorShaderProperty = Shader.PropertyToID("_OutlineColor");

    private void OnDisable()
    {
        _amuletButton.onClick.RemoveAllListeners();
    }

    public void Init(AmuletShopUIController controller, int index, AmuletSO amuletData, bool isUnlocked)
    {
        _controller = controller;
        _databaseIndex = index;
        _isUnlocked = isUnlocked;
        _amuletData = amuletData;

        if (_label != null)
            _label.text = amuletData.DisplayName;

        _amuletButton.onClick.RemoveAllListeners();
        _amuletButton.onClick.AddListener(() => _controller.PreviewAmulet(_amuletData, _databaseIndex, _isUnlocked));

        _border.material = new Material(_border.material);

        var idx = -1;
        foreach ((var key, var sprite) in _ressourcesSprite)
        {
            ++idx;
            if (amuletData.RessourcesPrice[key] <= 0)
                continue;
            
            var inst = Instantiate(_ressourceComponent, _ressourceComponentParent.transform);
            inst.Init(idx, sprite, amuletData.RessourcesPrice[key]);
        }

        Refresh(isUnlocked, false);
    }

    private void Refresh(bool isUnlock, bool isFocused)
    {
        _isUnlocked = isUnlock;
        _isFocused = isFocused;

        _icon.sprite = _amuletData.Icon;
        _ressourceComponentParent.SetActive(!isUnlock);
        
        RefreshColor();
    }

    public void SetFocus(bool value)
    {
        _isFocused = value;
        RefreshColor();
    }

    private void RefreshColor()
    {
        _label.color = GetCurrentOutlineColor(true);
        _icon.enabled = _isUnlocked;

        // todo @hyverno set background with rarity when integrate (BackgroundColorShaderProperty)

        Color targetColor = GetCurrentOutlineColor();
        if (_border.material != null)
        {
            _border.material.SetColor(OutlineColorShaderProperty, targetColor);
        }

        // _border.color = GetCurrentOutlineColor();

        Debug.Log(CpBaseUISettings.ComplementaryColor);
    }

    private Color GetCurrentOutlineColor(bool isText = false)
    {
        if (_isFocused)
        {
            if (isText)
                return CpBaseUISettings.ComplementaryColor;
            if (_isUnlocked)
                return CpBaseUISettings.MainColor;

            return CpBaseUISettings.SecondColor;
        }

        if (isText)
            return CpBaseUISettings.ComplementaryColorOver;
        if (_isUnlocked)
            return CpBaseUISettings.MainColorOver;

        return CpBaseUISettings.SecondColorOver;
    }
}