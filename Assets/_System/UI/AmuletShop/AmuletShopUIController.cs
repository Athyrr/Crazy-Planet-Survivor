using System;
using System.Linq;
using _System.ECS.Authorings.Ressources;
using UnityEngine;
using Unity.Entities;

public class AmuletShopUIController : ShopUIControllerBase<AmuletSO, AmuletListView, AmuletDetailView, AmuletViewItem>
{
    [Header("Database")] public AmuletsDatabaseSO Database;
    
    private EntityQuery _gameStateQuery;

    protected override void Awake()
    {
        base.Awake();
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
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
        if (_selectedItemIndex < 0 || _selectedItemIndex >= Database.Amulets.Length)
            return;

        if (!CanBuyAmulet(out var cost))
            return;

        BuyAmulet(cost);

        if (!_gameStateQuery.IsEmpty)
        {
            var gameStateEntity = _gameStateQuery.GetSingletonEntity();
            var unlockedBuffer = _entityManager.GetBuffer<UnlockedAmulet>(gameStateEntity);
            unlockedBuffer.Add(new UnlockedAmulet { DbIndex = _selectedItemIndex });
        }

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

    private void BuyAmulet(int[] cost)
    {
        var ressources = SaveManager.GetCurrentSaveAs<Save>().ressources.Ressources;

        foreach (var el in cost.ToList())
        {
            if (el != 0)
                ressources[el] -= GetDataAtIndex(_selectedItemIndex).RessourcesPrice[(ERessourceType)el];
        }
    }

    private bool CanBuyAmulet(out int[] cost)
    {
        var resources = SaveManager.GetCurrentSaveAs<Save>().ressources.Ressources;

        cost = new int[Enum.GetNames(typeof(ERessourceType)).Length];
        var res = true;

        foreach (var el in GetDataAtIndex(_selectedItemIndex).RessourcesPrice.ToList())
        {
            var idx = (int)el.Key;
            if (resources[idx] >= el.Value)
            {
                cost[(idx)] = idx;
            }
            else
            {
                res = false;
                break;
            }
        }

        return res;
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