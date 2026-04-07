using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Represents the Character Selection panel that managed playable character to play.
/// </summary>
public class CharacterSelectionShopUIController : ShopUIControllerBase<CharacterSO, CharacterListView,
    CharacterDetailView, CharacterViewItem>
{
    [Header("Characters database")] public CharactersDatabaseSO Database;

    [Header("UI Views")] public CharacterListView characterListView;
    public CharacterDetailView characterDetailView;
    public CharacterStatsListView characterStatsListView;
    
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
        ListView.Clear();
        for (int i = 0; i < GetItemsCount(); i++)
        {
            var data = GetDataAtIndex(i);
            var item = ListView.GetOrCreateItem();
            bool isUnlocked = IsCharacterUnlocked(i);

            // item.Init(this, i, data, isUnlocked);
        }
    }

    protected override void RefreshDetailView(CharacterSO data, int index)
    {
        _isSelectedItemUnlocked = IsCharacterUnlocked(index);
        DetailView.Refresh(data, _isSelectedItemUnlocked);
        RefreshActionButton();
        
        // todo stats view
        characterStatsListView.Refresh(GetDataAtIndex(index).coreStats);
    }

    private bool IsCharacterUnlocked(int index)
    {
        if (_gameStateQuery.IsEmptyIgnoreFilter)
            return false;

        // todo use buffer UnlockedCharacter (as Amulets)
        // var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        // if (!_entityManager.HasBuffer<UnlockedCharacter>(gameStateEntity))
        //     return false;
        //
        // var unlockedCharacters = _entityManager.GetBuffer<UnlockedCharacter>(gameStateEntity);
        //
        // foreach (UnlockedCharacter unlockedCharacter in unlockedCharacters)
        // {
        //     if (unlockedCharacter.DbIndex == index)
        //         return true;
        // }

        // return false;
        return true;
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
    

    public void ConfirmSelection()
    {
        ConfirmSelection(_selectedItemIndex);
    }

    private void ConfirmSelection(int index)
    {
        var requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(requestEntity, new SelectCharacterRequest
        {
            CharacterIndex = index
        });

        GameManager.Instance.ChangeState(EGameState.Lobby);
        //Debug.Log($"Select Character Index {index}");
    }
}