using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AmuletUIComponent : MonoBehaviour
{
    public Button AmuletButton;
    public TMP_Text Label;
    public Image Icon;
    public Image Border;

    public Color UnlockedTextColor = Color.white;
    public Color LockedTextColor = Color.gray;

    private AmuletShopUIController _controller;
    private AmuletSO _amuletData;
    private int _databaseIndex;
    private bool _isUnlocked;
    private bool _isFocused;

    private void OnDisable()
    {
        AmuletButton.onClick.RemoveAllListeners();
    }

    public void Init(AmuletShopUIController controller, int index, AmuletSO amuletData, bool isUnlocked)
    {
        _controller = controller;
        _databaseIndex = index;
        _isUnlocked = isUnlocked;
        _amuletData = amuletData;

        if (Label != null)
            Label.text = amuletData.DisplayName;

        AmuletButton.onClick.RemoveAllListeners();
        AmuletButton.onClick.AddListener(() => _controller.PreviewAmulet(_amuletData, _databaseIndex, _isUnlocked));

        Refresh(isUnlocked, false);
    }

    public void Refresh(bool isUnlock, bool isFocused)
    {
        _isUnlocked = isUnlock;
        _isFocused = isFocused;

        Icon.sprite = _amuletData.Icon;
        RefreshColor();
        SetBorderIcon(isFocused);
    }
    
    public void SetBorderIcon(bool visible)
    {
        if (Border != null)
            Border.enabled = visible;
    }

    private void RefreshColor()
    {
        Label.color = _isUnlocked ? UnlockedTextColor : LockedTextColor;
    }
}