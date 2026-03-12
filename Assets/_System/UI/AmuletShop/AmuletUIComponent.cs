using _System.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AmuletUIComponent : MonoBehaviour
{
    [SerializeField] private Button _amuletButton;
    [SerializeField] private TMP_Text _label;
    [SerializeField] private Image _icon;
    [SerializeField] private Image _border;

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

        // todo c'est giga chlag, le mieux c'est vertex color en SG et modifier border.color
        _border.material = new Material(_border.material);
        
        Refresh(isUnlocked, false);
    }

    private void Refresh(bool isUnlock, bool isFocused)
    {
        _isUnlocked = isUnlock;
        _isFocused = isFocused;

        _icon.sprite = _amuletData.Icon;
        RefreshColor();
    }

    public void SetFocus(bool value) => _isFocused = value;

    private void RefreshColor()
    {
        _label.color = GetCurrentOutlineColor(true);
        _icon.enabled = _isUnlocked;
        
        // todo @hyverno set background with rarity when integrate (BackgroundColorShaderProperty)
         // _border.material.SetColor(OutlineColorShaderProperty, GetCurrentOutlineColor());
         
         Color targetColor = GetCurrentOutlineColor();
         
         if (_border.material != null)
         {
             _border.material.SetColor(OutlineColorShaderProperty, targetColor);
         }
         
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