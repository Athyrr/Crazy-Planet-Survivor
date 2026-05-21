using Unity.Entities;
using UnityEngine;

/// <summary>
/// Main controller for the Meta-Progression shop UI.
/// Delegates data persistence to MetaProgressionManager.
/// </summary>
public class MetaProgressionController : MonoBehaviour
{
    [Header("Database")]
    [SerializeField] private MetaUpgradesDatabaseSO _database;
    [SerializeField] private ResourceDatabaseSO _resourceDatabase;

    [Header("Grid View")]
    [SerializeField] private Transform _gridContainer;
    [SerializeField] private MetaProgressionViewItem _gridItemPrefab;

    [Header("Detail View")]
    [SerializeField] private MetaProgressionDetailView _detailView;

    [Header("Navigation")]
    [SerializeField] private GameObject _mainView;

    private EntityManager _entityManager;
    private MetaProgressionViewItem[] _gridItems;
    private int _focusedIndex;
    private int _selectedIndex;

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
    }

    private void Start()
    {
        if (_detailView != null)
            _detailView.SetController(this);
    }

    public void OpenView()
    {
        if (_mainView != null)
            _mainView.SetActive(true);

        // Load data from save into the manager (POO)
        MetaProgressionManager.LoadFromSave();

        SyncBufferFromManager();
        BuildGrid();
        FocusItem(0);
        SelectItem(0);
    }

    /// <summary>
    /// Sync manager data → ECS buffer so ApplyMetaProgressionSystem can read it at run start.
    /// </summary>
    private void SyncBufferFromManager()
    {
        MetaProgressionManager.SyncToBuffer(_database);
    }

    public void CloseView()
    {
        if (_mainView != null)
            _mainView.SetActive(false);
    }

    private void BuildGrid()
    {
        if (_gridContainer == null || _gridItemPrefab == null || _database == null)
            return;

        foreach (Transform child in _gridContainer)
            Destroy(child.gameObject);

        int count = _database.Count;
        _gridItems = new MetaProgressionViewItem[count];

        for (int i = 0; i < count; i++)
        {
            var data = _database.Upgrades[i];
            if (data == null) continue;

            int level = MetaProgressionManager.GetLevel(data.TargetStat);

            var item = Instantiate(_gridItemPrefab, _gridContainer);
            item.Init(this, i, data, level);
            _gridItems[i] = item;
        }
    }

    public void FocusItem(int index)
    {
        if (index < 0 || index >= GetItemsCount())
            return;

        _focusedIndex = index;

        if (_gridItems != null)
        {
            for (int i = 0; i < _gridItems.Length; i++)
            {
                if (_gridItems[i] != null)
                    _gridItems[i].SetFocus(i == index);
            }
        }
    }

    public void SelectItem(int index)
    {
        if (index < 0 || index >= GetItemsCount())
            return;

        _selectedIndex = index;

        if (_gridItems != null)
        {
            for (int i = 0; i < _gridItems.Length; i++)
            {
                if (_gridItems[i] != null)
                    _gridItems[i].SetSelected(i == index);
            }
        }

        if (_detailView != null && _database != null)
        {
            var data = _database.Upgrades[index];
            int level = MetaProgressionManager.GetLevel(data.TargetStat);
            _detailView.Refresh(data, level, index, _resourceDatabase);
        }
    }

    public void PurchaseUpgrade()
    {
        PurchaseUpgrade(_selectedIndex);
    }

    public void PurchaseUpgrade(int index)
    {
        Debug.Log($"[MetaShop] PurchaseUpgrade({index}) start");

        if (index < 0 || index >= GetItemsCount())
        {
            Debug.LogWarning($"[MetaShop] Return: index {index} out of range (count {GetItemsCount()})");
            return;
        }

        if (_entityManager == null)
        {
            Debug.LogWarning("[MetaShop] Return: EntityManager null");
            return;
        }

        var data = _database.Upgrades[index];
        if (data == null)
        {
            Debug.LogWarning("[MetaShop] Return: data null");
            return;
        }

        int currentLevel = MetaProgressionManager.GetLevel(data.TargetStat);
        int maxLevel = data.BonusPerLevel != null ? data.BonusPerLevel.Length : 5;

        Debug.Log($"[MetaShop] stat={data.TargetStat} level={currentLevel}/{maxLevel}");

        if (currentLevel >= maxLevel)
        {
            Debug.Log("[MetaShop] Return: already maxed");
            return;
        }

        var query = _entityManager.CreateEntityQuery(typeof(GameState));
        if (query.IsEmpty)
        {
            Debug.LogWarning("[MetaShop] Return: GameState query empty");
            return;
        }

        var gameStateEntity = query.GetSingletonEntity();

        // Check resources
        var cost = data.GetCostForLevel(currentLevel);
        if (cost.Amount > 0)
        {
            var resourceBuffer = _entityManager.GetBuffer<ResourceBufferElement>(gameStateEntity);
            if (!resourceBuffer.HasEnough(new[] { cost }))
            {
                Debug.Log("[MetaShop] Return: not enough resources");
                return;
            }

            resourceBuffer.DeductCost(new[] { cost });
            resourceBuffer.Save();
        }

        // Upgrade in manager
        int newLevel = currentLevel + 1;
        MetaProgressionManager.SetLevel(data.TargetStat, newLevel);

        // Persist to save
        MetaProgressionManager.SaveToDisk();

        // Sync to buffer
        SyncBufferFromManager();

        // Refresh UI
        RefreshGridItem(index, newLevel);
        SelectItem(index);

        Debug.Log($"[MetaShop] Purchase done: {data.TargetStat} -> lvl {newLevel}");
    }

    private void RefreshGridItem(int index, int newLevel)
    {
        if (_gridItems != null && index >= 0 && index < _gridItems.Length && _gridItems[index] != null)
        {
            _gridItems[index].RefreshLevel(newLevel);
        }
    }

    public void BackToLobby()
    {
        CloseView();
        GameManager.Instance.ChangeState(EGameState.Lobby);
    }

    private int GetItemsCount() => _database != null ? _database.Count : 0;
}
