using System;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public abstract class ShopUIControllerBase<TData, TListView, TDetailView, TItem> : UIControllerBase
    where TData : ScriptableObject
    where TListView : ShopListViewBase<TItem>
    where TDetailView : ShopDetailViewBase<TData>
    where TItem : UIViewItemBase
{
    [Header("Views")] 
    public TListView ListView;
    public TDetailView DetailView;
    
    [Header("Button")]
    public Button ActionButton;
    public TMP_Text ActionButtonText;
    
    public string PurchaseText = String.Empty;
    public Color PurchaseColor = Color.yellow;

    public string ChooseText = String.Empty;
    public Color ChooseColor = Color.white;

    protected EntityManager _entityManager;
    
    protected int _selectedItemIndex = -1;
    protected int _focusedItemIndex = -1;

    protected bool _isSelectedItemUnlocked = false;

    private GameInputs _shopInputs;

    protected virtual void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
    }

    protected virtual void Start()
    {
        RefreshListView();
    }

    public virtual void OpenView()
    {
        ListView.OpenView();
        DetailView.OpenView();

        RefreshListView();

        // Force a fresh focus/preview on open so re-opening never shows a stale panel.
        _focusedItemIndex = -1;
        _selectedItemIndex = -1;

        int startIdx = GetStartingIndex();
        FocusItem(startIdx);
    }
    
    public virtual void CloseViews()
    {
        // todo animation
        
        ListView.CloseView();
        DetailView.CloseView();
    }
    
    public virtual void FocusItem(int index)
    {
        if (index < 0 || index >= GetItemsCount())
            return;

        if (index == _focusedItemIndex)
            return;

        _focusedItemIndex = index;
        ListView.SetFocused(index);

        // Focusing an item also previews/stages it; committing happens on BackToLobby().
        SelectItem(index);
    }

    public virtual void SelectItem(int index)
    {
        if (index < 0 || index >= GetItemsCount())
            return;
        
        _selectedItemIndex = index;
        
        // ListView.SetSelected(index);

        TData data = GetDataAtIndex(index);
        RefreshDetailView(data, index);
    }

    public virtual void Next()
    {
        FocusItem((_focusedItemIndex + 1) % GetItemsCount());
    }

    public virtual void Previous()
    {
        FocusItem((_focusedItemIndex - 1 + GetItemsCount()) % GetItemsCount());
    }
    
    public virtual void BackToLobby()
    {
        // Just leave — the focused selection is committed in HandleShopClosed() (OnDisable),
        // so it applies no matter how the player exits (Back button, Esc, or state change).
        GameManager.Instance.ChangeState(EGameState.Lobby);
    }

    /// <summary>Derived shops commit their focused selection here (no-op if locked/none/unchanged).</summary>
    protected virtual void CommitFocusedSelection() { }

    /// <summary>Whether the player can currently afford the item at <paramref name="index"/>.</summary>
    protected virtual bool CanAffordSelected(int index) => false;

    /// <summary>Wire Esc / gamepad-B (UI.Cancel) to BackToLobby while the shop is open.</summary>
    protected void EnableShopInput()
    {
        if (_shopInputs == null)
            _shopInputs = new GameInputs();

        _shopInputs.UI.Cancel.performed += OnCancelInput;
        _shopInputs.UI.Enable();
    }

    protected void DisableShopInput()
    {
        if (_shopInputs == null)
            return;

        _shopInputs.UI.Cancel.performed -= OnCancelInput;
        _shopInputs.UI.Disable();
    }

    /// <summary>
    /// Called from derived OnDisable when the shop closes. Disables input and commits the
    /// focused selection so it is applied no matter how the player left the shop.
    /// </summary>
    protected void HandleShopClosed()
    {
        DisableShopInput();

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        _entityManager = world.EntityManager;
        CommitFocusedSelection();
    }

    private void OnCancelInput(InputAction.CallbackContext ctx) => BackToLobby();

    protected virtual void RefreshActionButton()
    {
        if (ActionButton == null)
            return;

        // The purchase button is shown only when the focused item is locked AND affordable.
        // Unlocked items are chosen/equipped via BackToLobby(), not this button.
        bool showPurchase = !_isSelectedItemUnlocked && CanAffordSelected(_selectedItemIndex);
        ActionButton.gameObject.SetActive(showPurchase);

        if (showPurchase)
        {
            ActionButtonText.text = PurchaseText;
            ActionButtonText.color = PurchaseColor;
        }
    }
    
    protected abstract int GetItemsCount();
    protected abstract TData GetDataAtIndex(int index);
    protected abstract void RefreshListView();
    protected abstract void RefreshDetailView(TData data, int index);
    protected abstract int GetStartingIndex();
}