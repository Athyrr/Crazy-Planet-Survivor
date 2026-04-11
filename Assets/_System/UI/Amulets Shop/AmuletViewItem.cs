using _System.ECS.Authorings.Ressources;
using _System.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AmuletViewItem : UIViewItemBase
{
    [SerializeField] private Button _amuletButton;
    [SerializeField] private TMP_Text _label;
    [SerializeField] private Image _icon;
    [SerializeField] private Image _border;

    [SerializeField]
    private EnumValues<ERessourceType, Sprite>
        _ressourcesSprite; // todo @hyverno move in icon database with CPBaseSettings

    [SerializeField] private RessourceWidgetItem _ressourceComponent; // same
    [SerializeField] private GameObject _ressourceComponentParent;

    private AmuletShopUIController _controller;
    private int _databaseIndex;
    private bool _isUnlocked;
    private bool _isFocused;
    private bool _isSelected;

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

        _label.text = amuletData.DisplayName;
        _icon.sprite = amuletData.Icon;
        _ressourceComponentParent.SetActive(!isUnlocked);
        
        _amuletButton.onClick.RemoveAllListeners();
        _amuletButton.onClick.AddListener(() => _controller.SelectItem(_databaseIndex));

        _border.material = new Material(_border.material);
        RefreshColor();
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        Debug.Log("OnPointerEnter");
        // print if controller is null
        if (_controller == null)
            Debug.Log("Controller is null");
        
        _controller.FocusItem(_databaseIndex);
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        _controller.SelectItem(_databaseIndex);
    }

    public override void SetFocus(bool isFocused)
    {
        _isFocused = isFocused;
        RefreshColor();
    }

    public override void SetSelected(bool isSelected)
    {
        _isSelected = isSelected;
        RefreshColor();
    }

    private void RefreshColor()
    {
        _label.color = GetCurrentOutlineColor(true);
        _icon.enabled = _isUnlocked;

        // todo @hyverno set background with rarity when integrate (BackgroundColorShaderProperty)

        Color targetColor = GetCurrentOutlineColor();
        if (_border.material != null)
            _border.material.SetColor(OutlineColorShaderProperty, targetColor);

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