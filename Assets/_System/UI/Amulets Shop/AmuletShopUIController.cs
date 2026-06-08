using NfcPoc;
using Unity.Entities;
using UnityEngine;

public class AmuletShopUIController
    : ShopUIControllerBase<AmuletSO, AmuletShopListView, AmuletShopDetailView, AmuletViewItem>
{
    [Header("Database")] public AmuletsDatabaseSO Database;

    private EntityQuery _gameStateQuery;

    // One-shot flag: set right before a purchase-triggered refresh so the detail view plays the
    // delayed unlock reveal. Consumed (reset) in RefreshDetailView so plain selection switches
    // don't animate.
    private bool _animateNextUnlock;

    protected override EGameState ShopState => EGameState.AmuletShop;

    protected override void Awake()
    {
        base.Awake();
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
    }

    /// <summary>Controller confirm while committed: buy the locked item.</summary>
    protected override void ExecutePurchase(int index)
    {
        _selectedItemIndex = index;
        PurchaseAmulet();

        // The item is now unlocked (or the buy failed) — release the purchase focus either way.
        _committedItemIndex = -1;
        SetPurchaseFocused(false);
    }

    /// <summary>Controller confirm on an already-owned amulet: equip it and leave.</summary>
    protected override void ConfirmUnlockedItem(int index)
    {
        _selectedItemIndex = index;
        EquipAmulet();
    }

    protected override void Start()
    {
        base.Start();

        RefreshListView();
    }

    protected override int GetItemsCount() => Database != null ? Database.Amulets.Length : 0;

    protected override AmuletSO GetDataAtIndex(int index) => Database.Amulets[index];

    protected override void RefreshListView()
    {
        ListView.Clear();

        for (int i = 0; i < GetItemsCount(); i++)
        {
            var data = GetDataAtIndex(i);
            var item = ListView.GetOrCreateItem();
            bool isUnlocked = IsAmuletUnlocked(i);

            item.Init(this, i, data, isUnlocked);
        }
    }

    protected override void RefreshDetailView(AmuletSO data, int index)
    {
        _isSelectedItemUnlocked = IsAmuletUnlocked(index);

        bool animateUnlock = _animateNextUnlock;
        _animateNextUnlock = false;

        DetailView.Refresh(data, _isSelectedItemUnlocked, animateUnlock);
        RefreshActionButton();
    }

    protected override bool CanAffordSelected(int index)
    {
        if (_gameStateQuery.IsEmpty)
            return false;
        if (Database == null || index < 0 || index >= Database.Amulets.Length)
            return false;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        if (!_entityManager.HasBuffer<ResourceBufferElement>(gameStateEntity))
            return false;

        var resources = _entityManager.GetBuffer<ResourceBufferElement>(gameStateEntity);
        return resources.HasEnough(GetDataAtIndex(index).PurchaseCost);
    }

    protected override void CommitFocusedSelection()
    {
        // The focused (unlocked) amulet becomes the equipped one. A locked focus keeps the
        // last equipped amulet (EquippedAmulet is left untouched).
        if (_gameStateQuery.IsEmpty)
            return;
        if (!IsAmuletUnlocked(_focusedItemIndex))
            return;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();

        // Skip if it is already equipped.
        if (_entityManager.HasComponent<EquippedAmulet>(gameStateEntity) &&
            _entityManager.GetComponentData<EquippedAmulet>(gameStateEntity).DbIndex == _focusedItemIndex)
            return;

        _entityManager.SetComponentData(
            gameStateEntity,
            new EquippedAmulet { DbIndex = _focusedItemIndex }
        );
    }

    protected override int GetStartingIndex()
    {
        if (!_gameStateQuery.IsEmpty)
        {
            var entity = _gameStateQuery.GetSingletonEntity();
            if (_entityManager.HasComponent<EquippedAmulet>(entity))
            {
                int equipped = _entityManager.GetComponentData<EquippedAmulet>(entity).DbIndex;
                if (equipped >= 0)
                    return equipped;
            }
        }

        return 0;
    }

    public void PurchaseOrEquipAmulet()
    {
        if (_isSelectedItemUnlocked)
            EquipAmulet();
        else
            PurchaseAmulet();
    }

    private void PurchaseAmulet()
    {
        if (_gameStateQuery.IsEmpty)
            return;

        if (_selectedItemIndex < 0 || _selectedItemIndex >= Database.Amulets.Length)
            return;

        // Already owned — nothing to buy
        if (IsAmuletUnlocked(_selectedItemIndex))
            return;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        var metaResources = _entityManager.GetBuffer<ResourceBufferElement>(gameStateEntity);
        var cost = GetDataAtIndex(_selectedItemIndex).PurchaseCost;

        if (!metaResources.HasEnough(cost))
            return;

        metaResources.DeductCost(cost);
        metaResources.Save(); // todo maybe save only when Application Quit ?

        GrantAmulet(_selectedItemIndex);
    }

    /// <summary>
    /// Adds the amulet at <paramref name="index"/> to the unlocked set, rebuilds the list, and
    /// plays the full unlock reveal (delayed appearance + base-character reshow + VFX). Shared by
    /// both currency and NFC purchases so every buy looks the same.
    /// </summary>
    private void GrantAmulet(int index)
    {
        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        var unlockedBuffer = _entityManager.GetBuffer<UnlockedAmulet>(gameStateEntity);
        unlockedBuffer.Add(new UnlockedAmulet { DbIndex = index });

        RefreshListView();

        // Force a refresh to the now-unlocked state, focusing the just-bought amulet, and flag the
        // detail view to play the reveal animation on that refresh.
        _animateNextUnlock = true;
        _focusedItemIndex = -1;
        FocusItem(index);

        DetailView.PlayUnlockVfx();
    }

    #region NFC

    private void OnEnable()
    {
        EnableShopInput();

        NfcManager.OnTagDetected += HandleTagDetected;

        if (NfcManager.Instance.IsReading)
        {
            NfcManager.Instance.StopReading();
            Debug.Log("NFC stopped");
        }
        else
        {
            NfcManager.Instance.StartReading();
            Debug.Log("NFC started");

            Debug.Log("IsReading :" + NfcManager.Instance.IsReading);
        }
    }

    private void OnDisable()
    {
        HandleShopClosed();

        NfcManager.OnTagDetected -= HandleTagDetected;

        if (NfcManager.Instance.IsReading)
        {
            NfcManager.Instance.StopReading();
            Debug.Log("NFC stopped");
        }
    }

    private void PurchaseAmuletWithNfc(int index)
    {
        Debug.Log("Buying by nfc #bipbip");

        if (_gameStateQuery.IsEmpty)
            return;

        if (index < 0 || index >= Database.Amulets.Length)
            return;

        if (IsAmuletUnlocked(index))
        {
            SelectItem(index);
            return;
        }

        GrantAmulet(index);
    }

    private int FindAmuletIndexByName(string amuletName)
    {
        for (int i = 0; i < Database.Amulets.Length; i++)
        {
            if (Database.Amulets[i].DisplayName == amuletName)
                return i;
        }

        return -1;
    }

    private void HandleTagDetected(NfcTagData tagData)
    {
        if (tagData.NdefRecords.Count < 2)
            return;

        var typeRecord = tagData.NdefRecords[0];
        if (!typeRecord.Payload.Contains("Amulet"))
            return;

        var amuletName = tagData.NdefRecords[1].Payload;
        int index = FindAmuletIndexByName(amuletName);
        if (index < 0)
        {
            Debug.LogWarning($"No amulet found for NFC name '{amuletName}'");
            return;
        }

        PurchaseAmuletWithNfc(index);
    }

    #endregion

    private void EquipAmulet()
    {
        if (_gameStateQuery.IsEmpty || _selectedItemIndex == -1)
            return;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        _entityManager.SetComponentData(
            gameStateEntity,
            new EquippedAmulet() { DbIndex = _selectedItemIndex }
        );

        BackToLobby();
    }

    private bool IsAmuletUnlocked(int index)
    {
        if (_gameStateQuery.IsEmptyIgnoreFilter)
            return false;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        if (!_entityManager.HasBuffer<UnlockedAmulet>(gameStateEntity))
            return false;

        var unlockedAmulets = _entityManager.GetBuffer<UnlockedAmulet>(gameStateEntity);

        foreach (UnlockedAmulet unlockedAmulet in unlockedAmulets)
        {
            if (unlockedAmulet.DbIndex == index)
                return true;
        }

        return false;
    }
}