using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Main controller for the Meta-Progression shop UI. Delegates data persistence to
/// MetaProgressionManager.
///
/// Navigation mirrors the other shops:
/// <list type="bullet">
/// <item>Hover / controller-navigate <b>highlights</b> an upgrade (does not commit it).</item>
/// <item>Click / Interact (E / Enter / gamepad South) <b>commits</b> the highlighted upgrade and
/// focuses the purchase button; pressing Interact again purchases.</item>
/// <item>Cancel (Esc / gamepad B) removes the focus if an upgrade is committed, otherwise returns
/// to the lobby.</item>
/// </list>
/// </summary>
public class MetaProgressionController : MonoBehaviour
{
    [Header("Database")]
    [SerializeField] private MetaUpgradesDatabaseSO _database;
    [SerializeField] private ResourceDatabaseSO _resourceDatabase;

    [Header("Grid View")]
    [SerializeField] private Transform _gridContainer;
    [SerializeField] private MetaProgressionViewItem _gridItemPrefab;

    [Tooltip("Columns used for controller up/down navigation. 0 = read the GridLayoutGroup constraint.")]
    [SerializeField] private int _gridColumns = 0;

    [Header("Detail View")]
    [SerializeField] private MetaProgressionDetailView _detailView;

    [Header("Navigation")]
    [SerializeField] private GameObject _mainView;

    [Header("Animation")]
    [Tooltip("Panel entrance/exit animators (UISlidePanel / UIFadePanel) driven as a group on open " +
             "and close — e.g. grid from left, detail from bottom, title from top, background fades.")]
    [SerializeField] private MonoBehaviour[] _panelAnimators;

    private EntityManager _entityManager;
    private EntityQuery _gameStateQuery;

    private MetaProgressionViewItem[] _gridItems;

    private int _focusedIndex = -1;     // hovered / cursor item (highlight + detail preview)
    private int _committedIndex = -1;   // committed item (purchase button focused); -1 = none

    private GameInputs _inputs;
    private bool _navAxisActive;
    private bool _prevSendNavEvents = true;

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
    }

    private void Start()
    {
        if (_detailView != null)
            _detailView.SetController(this);
    }

    private void OnEnable()
    {
        if (_inputs == null)
            _inputs = new GameInputs();

        _inputs.UI.Navigate.performed += OnNavigate;
        _inputs.UI.Cancel.performed += OnCancel;
        _inputs.UI.Enable();

        // Confirm uses the Interact action (E / Enter / gamepad South), like the other shops.
        _inputs.Player.Interact.performed += OnConfirm;
        _inputs.Player.Interact.Enable();
    }

    private void OnDisable()
    {
        if (_inputs != null)
        {
            _inputs.UI.Navigate.performed -= OnNavigate;
            _inputs.UI.Cancel.performed -= OnCancel;
            _inputs.UI.Disable();

            _inputs.Player.Interact.performed -= OnConfirm;
            _inputs.Player.Interact.Disable();
        }

        // Restore the EventSystem navigation routing we disabled while the shop was open.
        if (EventSystem.current != null)
            EventSystem.current.sendNavigationEvents = _prevSendNavEvents;
    }

    public void OpenView()
    {
        if (_mainView != null)
            _mainView.SetActive(true);

        // Animate the panels in (the panel is active here).
        UIPanelGroup.Show(_panelAnimators);

        // Ensure the detail view is wired even on the very first open (Start may not have run yet).
        if (_detailView != null)
            _detailView.SetController(this);

        // Load data from save into the manager (POO)
        MetaProgressionManager.LoadFromSave();

        SyncBufferFromManager();
        BuildGrid();

        _committedIndex = -1;
        _focusedIndex = -1;
        _navAxisActive = false;

        // Disable the EventSystem's navigation/submit routing so our manual controller flow can't
        // double-fire the purchase button (Interact handles confirm; pointer events still work for
        // mouse + touch).
        if (EventSystem.current != null)
        {
            _prevSendNavEvents = EventSystem.current.sendNavigationEvents;
            EventSystem.current.sendNavigationEvents = false;
            EventSystem.current.SetSelectedGameObject(null);
        }

        FocusItem(0);
    }

    public void CloseView()
    {
        if (_mainView != null)
            _mainView.SetActive(false);

        if (_detailView != null)
            _detailView.SetPurchaseFocused(false);

        if (EventSystem.current != null)
            EventSystem.current.sendNavigationEvents = _prevSendNavEvents;
    }

    /// <summary>
    /// Animated close used by the lobby state machine: slides the panel out, then deactivates the
    /// controller GameObject (which fires OnDisable → input + EventSystem restore). Falls back to an
    /// immediate deactivate when no slide animator is assigned. No-op if already inactive.
    /// </summary>
    public void CloseAnimated()
    {
        if (!gameObject.activeSelf)
            return;

        if (_detailView != null)
            _detailView.SetPurchaseFocused(false);

        UIPanelGroup.Hide(_panelAnimators, () => gameObject.SetActive(false));
    }

    /// <summary>
    /// Sync manager data → ECS buffer so ApplyMetaProgressionSystem can read it at run start.
    /// </summary>
    private void SyncBufferFromManager()
    {
        MetaProgressionManager.SyncToBuffer(_database);
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
            item.Init(this, i, data, level, CanAfford(i));
            _gridItems[i] = item;
        }
    }


    /// <summary>
    /// Highlights an upgrade (controller navigation or pointer hover) and previews it in the detail
    /// panel. Does not commit it. Locked while another upgrade is committed (Cancel to release).
    /// </summary>
    public void FocusItem(int index)
    {
        if (index < 0 || index >= GetItemsCount())
            return;

        // While an item is committed, hover/navigation is locked — Cancel (B / Esc) to release it.
        if (_committedIndex >= 0 && index != _committedIndex)
            return;

        _focusedIndex = index;

        if (_gridItems != null)
        {
            for (int i = 0; i < _gridItems.Length; i++)
                if (_gridItems[i] != null)
                {
                    _gridItems[i].SetHovered(false);
                    _gridItems[i].SetFocus(i == index);
                }
        }

        RefreshDetail(index);
    }

    /// <summary>Pointer hover (PC): highlights an upgrade only — no detail change, no commit.</summary>
    public void HoverItem(int index)
    {
        if (_gridItems == null)
            return;

        for (int i = 0; i < _gridItems.Length; i++)
            if (_gridItems[i] != null)
                _gridItems[i].SetHovered(i == index);
    }

    public void UnhoverItem(int index)
    {
        if (_gridItems == null)
            return;

        for (int i = 0; i < _gridItems.Length; i++)
            if (_gridItems[i] != null)
                _gridItems[i].SetHovered(false);
    }


    /// <summary>
    /// Commits the upgrade at <paramref name="index"/> (mouse click or Interact): marks it selected,
    /// shows its detail, and focuses the purchase button so the next Interact buys it.
    /// </summary>
    public void CommitItem(int index)
    {
        if (index < 0 || index >= GetItemsCount())
            return;

        _committedIndex = index;
        _focusedIndex = index;

        if (_gridItems != null)
        {
            for (int i = 0; i < _gridItems.Length; i++)
            {
                if (_gridItems[i] == null) continue;
                _gridItems[i].SetFocus(i == index);
                _gridItems[i].SetSelected(i == index);
            }
        }

        RefreshDetail(index);

        if (_detailView != null)
            _detailView.SetPurchaseFocused(true);
    }

    /// <summary>Releases the committed upgrade and returns control to grid navigation.</summary>
    public void UncommitItem()
    {
        if (_committedIndex < 0)
            return;

        int previous = _committedIndex;
        _committedIndex = -1;

        if (_gridItems != null)
        {
            for (int i = 0; i < _gridItems.Length; i++)
                if (_gridItems[i] != null)
                    _gridItems[i].SetSelected(false);
        }

        if (_detailView != null)
            _detailView.SetPurchaseFocused(false);

        // Keep the cursor on the previously committed item.
        FocusItem(previous);
    }

    private void RefreshDetail(int index)
    {
        if (_detailView == null || _database == null)
            return;
        if (index < 0 || index >= GetItemsCount())
            return;

        var data = _database.Upgrades[index];
        int level = MetaProgressionManager.GetLevel(data.TargetStat);
        _detailView.Refresh(data, level, index, _resourceDatabase);
    }

    // ---------- Purchase ----------

    /// <summary>Wired to the purchase button (UnityEvent) and the Interact confirm.</summary>
    public void PurchaseUpgrade()
    {
        PurchaseUpgrade(_committedIndex >= 0 ? _committedIndex : _focusedIndex);
    }

    public void PurchaseUpgrade(int index)
    {
        if (index < 0 || index >= GetItemsCount())
            return;

        if (_entityManager == null)
            return;

        var data = _database.Upgrades[index];
        if (data == null)
            return;

        int currentLevel = MetaProgressionManager.GetLevel(data.TargetStat);
        int maxLevel = data.BonusPerLevel != null ? data.BonusPerLevel.Length : 5;

        if (currentLevel >= maxLevel)
            return;

        if (_gameStateQuery.IsEmptyIgnoreFilter)
            return;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();

        // Check resources
        var cost = data.GetCostForLevel(currentLevel);
        if (cost.Amount > 0)
        {
            var resourceBuffer = _entityManager.GetBuffer<ResourceBufferElement>(gameStateEntity);
            if (!resourceBuffer.HasEnough(new[] { cost }))
                return;

            resourceBuffer.DeductCost(new[] { cost });
            resourceBuffer.Save();
        }

        // Upgrade in manager + persist + sync
        int newLevel = currentLevel + 1;
        MetaProgressionManager.SetLevel(data.TargetStat, newLevel);
        MetaProgressionManager.SaveToDisk();
        SyncBufferFromManager();

        // Refresh UI: the purchased item, its detail, and every item's affordability (resources changed).
        RefreshGridItem(index, newLevel);
        RefreshDetail(index);
        RefreshAllAffordability();

        // Nothing more to buy on this upgrade → release the focus back to the grid.
        if (newLevel >= maxLevel && _committedIndex == index)
            UncommitItem();
    }

    private void RefreshGridItem(int index, int newLevel)
    {
        if (_gridItems != null && index >= 0 && index < _gridItems.Length && _gridItems[index] != null)
            _gridItems[index].RefreshLevel(newLevel, CanAfford(index));
    }

    private void RefreshAllAffordability()
    {
        if (_gridItems == null)
            return;

        for (int i = 0; i < _gridItems.Length; i++)
            if (_gridItems[i] != null)
                _gridItems[i].SetAffordable(CanAfford(i));
    }

    /// <summary>Whether the player can afford the next level of the upgrade at <paramref name="index"/>.</summary>
    public bool CanAfford(int index)
    {
        if (_database == null || index < 0 || index >= GetItemsCount())
            return false;

        var data = _database.Upgrades[index];
        if (data == null)
            return false;

        int level = MetaProgressionManager.GetLevel(data.TargetStat);
        int maxLevel = data.BonusPerLevel != null ? data.BonusPerLevel.Length : 5;
        if (level >= maxLevel)
            return false; // maxed: no next level to buy

        var cost = data.GetCostForLevel(level);
        if (cost.Amount <= 0)
            return true; // free

        if (_gameStateQuery.IsEmptyIgnoreFilter)
            return false;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        if (!_entityManager.HasBuffer<ResourceBufferElement>(gameStateEntity))
            return false;

        var resources = _entityManager.GetBuffer<ResourceBufferElement>(gameStateEntity);
        return resources.HasEnough(new[] { cost });
    }

    public void BackToLobby()
    {
        // Don't CloseView() here — that would instantly hide the panel and skip the slide-out.
        // The lobby state machine runs CloseAnimated() on the state change (OnDisable then restores
        // input / EventSystem routing at the end of the slide).
        GameManager.Instance.ChangeState(EGameState.Lobby);
    }

    // ---------- Input ----------

    private static bool IsSelecting() =>
        GameManager.Instance != null && GameManager.Instance.GetGameState() == EGameState.MetaProgression;

    private void OnNavigate(InputAction.CallbackContext ctx)
    {
        if (!IsSelecting())
            return;

        int count = GetItemsCount();
        if (count == 0)
            return;

        Vector2 v = ctx.ReadValue<Vector2>();

        // Edge-triggered: one step per stick push / key press (release near center to step again).
        if (v.sqrMagnitude < 0.3f * 0.3f)
        {
            _navAxisActive = false;
            return;
        }

        if (_navAxisActive)
            return;
        _navAxisActive = true;

        // Navigating releases a committed purchase focus and moves to another upgrade.
        if (_committedIndex >= 0)
        {
            _committedIndex = -1;
            if (_gridItems != null)
                for (int i = 0; i < _gridItems.Length; i++)
                    if (_gridItems[i] != null)
                        _gridItems[i].SetSelected(false);
            if (_detailView != null)
                _detailView.SetPurchaseFocused(false);
        }

        int cols = GetColumns();
        int start = _focusedIndex < 0 ? 0 : _focusedIndex;
        int next;

        if (Mathf.Abs(v.x) >= Mathf.Abs(v.y))
            next = start + (v.x > 0 ? 1 : -1);          // horizontal
        else
            next = start + (v.y < 0 ? cols : -cols);    // vertical (stick up = previous row)

        next = ((next % count) + count) % count;
        FocusItem(next);
    }

    private void OnConfirm(InputAction.CallbackContext ctx)
    {
        if (!IsSelecting())
            return;

        if (_committedIndex >= 0)
            PurchaseUpgrade(_committedIndex);   // already focused on the purchase button → buy
        else if (_focusedIndex >= 0)
            CommitItem(_focusedIndex);          // commit the hovered upgrade → focus purchase button
    }

    private void OnCancel(InputAction.CallbackContext ctx)
    {
        if (!IsSelecting())
            return;

        if (_committedIndex >= 0)
            UncommitItem();   // remove the focus, back to grid navigation
        else
            BackToLobby();    // nothing focused → leave
    }

    private int GetColumns()
    {
        if (_gridColumns > 0)
            return _gridColumns;

        if (_gridContainer != null)
        {
            var grid = _gridContainer.GetComponent<GridLayoutGroup>();
            if (grid != null && grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
                return Mathf.Max(1, grid.constraintCount);
        }

        return 1;
    }

    private int GetItemsCount() => _database != null ? _database.Count : 0;
}
