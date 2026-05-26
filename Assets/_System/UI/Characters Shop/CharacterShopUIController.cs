using _System.ECS.Authorings.Resources;
using NfcPoc;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Represents the Character Selection panel that managed playable character to play.
/// </summary>
public class CharacterShopUIController
    : ShopUIControllerBase<
        CharacterSO,
        CharacterShopListView,
        CharacterShopDetailView,
        CharacterShopViewItem
    >
{
    [Header("Characters database")]
    public CharactersDatabaseSO Database;

    [Header("UI Views")]
    public CharacterShopListView characterShopListView;
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
        if (_selectedItemIndex == -1)
            return;

        CloseViews();

        var requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(
            requestEntity,
            new SelectCharacterRequest { CharacterIndex = _selectedItemIndex }
        );

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
        _entityManager.AddComponentData(
            requestEntity,
            new SelectCharacterRequest { CharacterIndex = index }
        );

        GameManager.Instance.ChangeState(EGameState.Lobby);
    }

    #region NFC


    private void OnEnable()
    {
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

        if (index < 0 || index >= Database.Characters.Length)
            return;

        if (IsCharacterUnlocked(index))
        {
            SelectItem(index);
            DetailView.PlayUnlockVfx();
            return;
        }

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();

        var unlockedBuffer = _entityManager.GetBuffer<UnlockedCharacter>(gameStateEntity);
        unlockedBuffer.Add(new UnlockedCharacter { DbIndex = index });

        RefreshListView();
        SelectItem(index);
        DetailView.PlayUnlockVfx();
    }

    private int FindAmuletIndexByName(string amuletName)
    {
        for (int i = 0; i < Database.Characters.Length; i++)
        {
            if (Database.Characters[i].DisplayName == amuletName)
                return i;
        }
        return -1;
    }

    private void HandleTagDetected(NfcTagData tagData)
    {
        if (tagData.NdefRecords.Count < 2)
            return;

        var typeRecord = tagData.NdefRecords[0];
        if (!typeRecord.Payload.Contains("Character"))
            return;

        var characterName = tagData.NdefRecords[1].Payload;
        int index = FindAmuletIndexByName(characterName);
        if (index < 0)
        {
            Debug.LogWarning($"No character found for NFC name '{characterName}'");
            return;
        }

        PurchaseAmuletWithNfc(index);
    }

    #endregion
}
