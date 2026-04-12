using System;
using TMPro;
using Unity.Entities;
using UnityEngine;
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
    private int _focusedItemIndex = -1;
    
    protected bool _isSelectedItemUnlocked = false;

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

        // RefreshListView();

        int startIdx = GetStartingIndex();
        FocusItem(startIdx);
        SelectItem(startIdx);
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
        
        _focusedItemIndex = index;
        ListView.SetFocused(index);
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
    
    public void BackToLobby() => GameManager.Instance.ChangeState(EGameState.Lobby);
    
    protected virtual void RefreshActionButton()
    {
        if (ActionButton == null)
            return;

        ActionButton.gameObject.SetActive(true);
        ActionButtonText.text = _isSelectedItemUnlocked ? ChooseText : PurchaseText;
        ActionButtonText.color = _isSelectedItemUnlocked ? ChooseColor : PurchaseColor;
    }
    
    protected abstract int GetItemsCount();
    protected abstract TData GetDataAtIndex(int index);
    protected abstract void RefreshListView();
    protected abstract void RefreshDetailView(TData data, int index);
    protected abstract int GetStartingIndex();
}