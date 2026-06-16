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

    // One-shot flag: set right before a purchase-triggered refresh so the detail view plays the
    // delayed unlock reveal. Consumed (reset) in RefreshDetailView so plain selection switches
    // don't animate.
    private bool _animateNextUnlock;

    protected override EGameState ShopState => EGameState.CharacterSelection;

    protected override void Awake()
    {
        base.Awake();
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
    }

    /// <summary>Controller confirm while committed: buy the locked character.</summary>
    protected override void ExecutePurchase(int index)
    {
        _selectedItemIndex = index;
        PurchaseCharacter();

        // The character is now unlocked (or the buy failed) — release the purchase focus either way.
        _committedItemIndex = -1;
        SetPurchaseFocused(false);
    }

    /// <summary>Controller confirm on an already-owned character: select it and leave.</summary>
    protected override void ConfirmUnlockedItem(int index)
    {
        _selectedItemIndex = index;
        SelectAndConfirm();
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

        bool animateUnlock = _animateNextUnlock;
        _animateNextUnlock = false;

        DetailView.Refresh(data, _isSelectedItemUnlocked, animateUnlock);
        RefreshActionButton();
    }

    protected override bool CanAffordSelected(int index)
    {
        if (_gameStateQuery.IsEmpty)
            return false;
        if (Database == null || index < 0 || index >= Database.Characters.Length)
            return false;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        if (!_entityManager.HasBuffer<ResourceBufferElement>(gameStateEntity))
            return false;

        var resources = _entityManager.GetBuffer<ResourceBufferElement>(gameStateEntity);
        return resources.HasEnough(GetDataAtIndex(index).PurchaseCost);
    }

    protected override void CommitFocusedSelection()
    {
        // Choosing a character: the focused (unlocked) character becomes the played one.
        // A locked focus changes nothing.
        if (_gameStateQuery.IsEmpty)
            return;
        if (!IsCharacterUnlocked(_focusedItemIndex))
            return;

        var entity = _gameStateQuery.GetSingletonEntity();

        // Skip if it is already the selected character (avoids a needless respawn).
        if (
            _entityManager.HasComponent<SelectedCharacter>(entity)
            && _entityManager.GetComponentData<SelectedCharacter>(entity).DbIndex
                == _focusedItemIndex
        )
            return;

        var requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(
            requestEntity,
            new SelectCharacterRequest { CharacterIndex = _focusedItemIndex }
        );
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

        // Already owned — nothing to buy (guards against a stale commit + button click).
        if (IsCharacterUnlocked(_selectedItemIndex))
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

        int purchasedIndex = _selectedItemIndex;
        RefreshListView();

        // Focus the just-purchased character first (force a refresh so the panel flips to its
        // now-unlocked state and it becomes the staged choice), and flag the detail view to play the
        // delayed reveal on that refresh. This refresh calls DetailView.Clear(), which destroys every
        // child of CharacterPreviewContainer — so the unlock VFX MUST be played AFTER it, otherwise the
        // freshly-spawned VFX (parented to that container) is destroyed in the same frame and never
        // shows. (This matches the amulet shop and the NFC path order.)
        _animateNextUnlock = true;
        _focusedItemIndex = -1;
        FocusItem(purchasedIndex);

        DetailView.PlayUnlockVfx();
    }

    private void SelectAndConfirm()
    {
        if (_selectedItemIndex == -1)
            return;

        CloseViews();

        // CommitFocusedSelection() sends the SelectCharacterRequest when the shop closes (OnDisable),
        // so just leave to the lobby — creating one here too would queue a duplicate.
        GameManager.Instance.ChangeState(EGameState.Lobby);
    }

    public void ConfirmSelection()
    {
        CloseViews();

        // CommitFocusedSelection() sends the SelectCharacterRequest when the shop closes (OnDisable),
        // so just leave to the lobby — creating one here too would queue a duplicate.
        GameManager.Instance.ChangeState(EGameState.Lobby);
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
        _animateNextUnlock = true;
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
