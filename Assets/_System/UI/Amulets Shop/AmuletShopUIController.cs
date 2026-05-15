using Unity.Entities;
using UnityEngine;

public class AmuletShopUIController : ShopUIControllerBase<AmuletSO, AmuletShopListView, AmuletShopDetailView, AmuletViewItem>
{
    [Header("Database")] public AmuletsDatabaseSO Database;

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
        // log item count 
        Debug.Log("amulet count" + GetItemsCount());

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

    protected override int GetStartingIndex()
    {
        if (!_gameStateQuery.IsEmpty)
        {
            var entity = _gameStateQuery.GetSingletonEntity();
            if (_entityManager.HasComponent<EquippedAmulet>(entity))
                return _entityManager.GetComponentData<EquippedAmulet>(entity).DbIndex;
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

        RefreshListView();
        SelectItem(_selectedItemIndex);
    }

    private void EquipAmulet()
    {
        if (_gameStateQuery.IsEmpty || _selectedItemIndex == -1)
            return;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        _entityManager.SetComponentData(gameStateEntity, new EquippedAmulet() { DbIndex = _selectedItemIndex });

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