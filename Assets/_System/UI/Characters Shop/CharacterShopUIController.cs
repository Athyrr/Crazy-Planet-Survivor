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
        CloseViews();
        
        var requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(requestEntity, new SelectCharacterRequest
        {
            CharacterIndex = index
        });

        GameManager.Instance.ChangeState(EGameState.Lobby);
        //Debug.Log($"Select Character Index {index}");
    }
}