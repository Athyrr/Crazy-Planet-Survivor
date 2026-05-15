using _System.ECS.Authorings.Resources;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Represents the Character Selection panel that managed playable character to play.
/// </summary>
public class CharacterShopUIController : ShopUIControllerBase<CharacterSO, CharacterShopListView,
    CharacterShopDetailView, CharacterShopViewItem>
{
    [Header("Characters database")] public CharactersDatabaseSO Database;

    [Header("UI Views")] public CharacterShopListView characterShopListView;
    public CharacterShopDetailView characterShopDetailView;

    private EntityQuery _gameStateQuery;

    
    
    protected override void Awake()
    {
        base.Awake();
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
    }

    protected override int GetItemsCount() => Database != null ? Database.Characters.Length : 0;

    protected override CharacterSO GetDataAtIndex(int index) => Database.Characters[index];

    protected override void RefreshListView()
    {
        ListView.Init(this, Database, IsCharacterUnlocked);
    }

    protected override void RefreshDetailView(CharacterSO data, int index)
    {
        _isSelectedItemUnlocked = IsCharacterUnlocked(index);
        DetailView.Refresh(data, _isSelectedItemUnlocked);
        RefreshActionButton();
    }

    private bool IsCharacterUnlocked(int index)
    {
        if (_gameStateQuery.IsEmptyIgnoreFilter)
            return false;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        if (!_entityManager.HasBuffer<UnlockedCharacter>(gameStateEntity))
            return false;

        var unlockedCharacters = _entityManager.GetBuffer<UnlockedCharacter>(gameStateEntity);

        foreach (var unlockedCharacter in unlockedCharacters)
        {
            if (unlockedCharacter.DbIndex == index)
                return true;
        }

        return false;
    }

    protected override int GetStartingIndex()
    {
        if (!_gameStateQuery.IsEmpty)
        {
            var entity = _gameStateQuery.GetSingletonEntity();
            if (_entityManager.HasComponent<SelectedCharacter>(entity))
                return _entityManager.GetComponentData<SelectedCharacter>(entity).DbIndex;
        }

        return 0;
    }

    public void PurchaseOrEquipCharacter()
    {
        if (_isSelectedItemUnlocked)
            SelectAndConfirm();
        else
            PurchaseCharacter();
    }

    private void PurchaseCharacter()
    {
        if (_gameStateQuery.IsEmpty)
            return;

        if (_selectedItemIndex < 0 || _selectedItemIndex >= Database.Characters.Length)
            return;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        var metaResources = _entityManager.GetBuffer<ResourceBufferElement>(gameStateEntity);
        var cost = GetDataAtIndex(_selectedItemIndex).PurchaseCost;

        if (!metaResources.HasEnough(cost))
            return;

        metaResources.DeductCost(cost);
        metaResources.Save();

        var unlockedBuffer = _entityManager.GetBuffer<UnlockedCharacter>(gameStateEntity);
        unlockedBuffer.Add(new UnlockedCharacter { DbIndex = _selectedItemIndex });

        _isSelectedItemUnlocked = true;

        RefreshListView();
        SelectItem(_selectedItemIndex);
    }

    private void SelectAndConfirm()
    {
        if (_selectedItemIndex == -1) return;

        CloseViews();

        var requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(requestEntity, new SelectCharacterRequest
        {
            CharacterIndex = _selectedItemIndex
        });

        GameManager.Instance.ChangeState(EGameState.Lobby);
    }

    public void ConfirmSelection()
    {
        ConfirmSelection(_selectedItemIndex);
    }

    private void ConfirmSelection(int index)
    {
        CloseViews();
        
        var requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(requestEntity, new SelectCharacterRequest
        {
            CharacterIndex = index
        });

        GameManager.Instance.ChangeState(EGameState.Lobby);
    }
}