using System;
using System.Text;
using NfcPoc;
using Unity.Entities;
using UnityEngine;

public class AmuletShopUIController
    : ShopUIControllerBase<AmuletSO, AmuletShopListView, AmuletShopDetailView, AmuletViewItem>
{
    [Header("Database")]
    public AmuletsDatabaseSO Database;

    private EntityQuery _gameStateQuery;

    protected override void Awake()
    {
        base.Awake();
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
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
        DetailView.Refresh(data, _isSelectedItemUnlocked);
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

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        var metaResources = _entityManager.GetBuffer<ResourceBufferElement>(gameStateEntity);
        var cost = GetDataAtIndex(_selectedItemIndex).PurchaseCost;

        if (!metaResources.HasEnough(cost))
            return;

        metaResources.DeductCost(cost);
        metaResources.Save(); // todo maybe save only when Application Quit ?

        var unlockedBuffer = _entityManager.GetBuffer<UnlockedAmulet>(gameStateEntity);
        unlockedBuffer.Add(new UnlockedAmulet { DbIndex = _selectedItemIndex });

        _isSelectedItemUnlocked = true;

        int purchasedIndex = _selectedItemIndex;
        RefreshListView();

        // Focus the just-purchased amulet (force a refresh to its now-unlocked state).
        _focusedItemIndex = -1;
        FocusItem(purchasedIndex);
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
            //DetailView.PlayUnlockVfx();
            return;
        }

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();

        var unlockedBuffer = _entityManager.GetBuffer<UnlockedAmulet>(gameStateEntity);
        unlockedBuffer.Add(new UnlockedAmulet { DbIndex = index });

        RefreshListView();
        SelectItem(index);
        DetailView.PlayUnlockVfx();
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
