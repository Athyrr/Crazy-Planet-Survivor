using System;
using _System.Settings;
using PrimeTween;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Base shop controller. Drives a single navigable list + a detail panel with one consistent
/// interaction model across mouse, keyboard and gamepad:
///
/// <list type="bullet">
/// <item><b>Hover</b> (PC mouse) — highlights the item only. Does not change the detail panel.</item>
/// <item><b>Focus</b> (click on PC, navigation on controller) — highlights and shows the item details.</item>
/// <item><b>Confirm</b> on a focused item — if it can be purchased, focuses the purchase button
/// (commit); if it is already owned, equips it and leaves; otherwise a rejected feedback hook fires.</item>
/// <item><b>Confirm</b> while committed — buys the item.</item>
/// <item><b>Cancel</b> (Esc / gamepad B) — releases a committed purchase focus, else returns to the lobby.</item>
/// </list>
///
/// Navigation is handled explicitly (not via the EventSystem) so the list is usable the instant the
/// shop opens and stays usable after a purchase rebuilds the list.
/// </summary>
public abstract class ShopUIControllerBase<TData, TListView, TDetailView, TItem> : UIControllerBase
    where TData : ScriptableObject
    where TListView : ShopListViewBase<TItem>
    where TDetailView : ShopDetailViewBase<TData>
    where TItem : UIViewItemBase
{
    [Header("Views")] public TListView ListView;
    public TDetailView DetailView;

    [Header("Animation")]
    [Tooltip("Panel entrance/exit animators (UISlidePanel / UIFadePanel) driven as a group on open " +
             "and close — e.g. list from left, stats from right, detail from bottom, title from top, " +
             "and the background fades. Slid/faded out (then the shop deactivates) on close.")]
    public MonoBehaviour[] PanelAnimators;

    [Header("Button")] public Button ActionButton;
    public TMP_Text ActionButtonText;

    [Tooltip("Optional highlight (frame/glow) shown when the purchase button is focused via controller.")]
    public GameObject PurchaseFocusHighlight;

    [Tooltip("Scale applied to the purchase button while it is focused (controller feedback).")]
    public float PurchaseFocusScale = 1.1f;

    public string PurchaseText = String.Empty;
    public Color PurchaseColor = Color.yellow;

    public string ChooseText = String.Empty;
    public Color ChooseColor = Color.white;

    [Tooltip("Columns of the list, used for controller up/down navigation. 1 = a linear list " +
             "(up/down step by one, like left/right).")]
    public int Columns = 1;

    protected EntityManager _entityManager;

    protected int _selectedItemIndex = -1; // item whose details are shown (== focused item)
    protected int _focusedItemIndex = -1; // navigation cursor / clicked item
    protected int _committedItemIndex = -1; // purchase-focused item (-1 = none)

    protected bool _isSelectedItemUnlocked = false;

    private GameInputs _shopInputs;
    private bool _navAxisActive;
    private bool _prevSendNavEvents = true;
    private Tween _purchaseFocusTween;

    private const float NavDeadzone = 0.3f;

    /// <summary>The game state this shop lives in, used to gate input handling.</summary>
    protected abstract EGameState ShopState { get; }

    protected virtual void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
    }

    protected virtual void Start()
    {
        RefreshListView();
    }

    public virtual void OpenView()
    {
        ListView.OpenView();
        DetailView.OpenView();

        // Animate the panels in (the GameObject is already active here).
        UIPanelGroup.Show(PanelAnimators);

        RefreshListView();

        // Force a fresh focus/preview on open so re-opening never shows a stale panel.
        _focusedItemIndex = -1;
        _selectedItemIndex = -1;
        _committedItemIndex = -1;
        _navAxisActive = false;

        SetPurchaseFocused(false);

        // Drive navigation ourselves: disable the EventSystem's navigation/submit routing so a
        // controller press can't double-fire the purchase button, and clear any stale selection.
        if (EventSystem.current != null)
        {
            _prevSendNavEvents = EventSystem.current.sendNavigationEvents;
            EventSystem.current.sendNavigationEvents = false;
            EventSystem.current.SetSelectedGameObject(null);
        }

        FocusItem(GetStartingIndex());
    }

    public virtual void CloseViews()
    {
        SetPurchaseFocused(false);

        ListView.CloseView();
        DetailView.CloseView();
    }

    /// <summary>
    /// Animated close used by the lobby state machine: runs <see cref="OnClosing"/> immediately (so
    /// in-flight effects stop the instant the player leaves), animates the panels out, then deactivates
    /// the GameObject (which fires OnDisable → HandleShopClosed for input/EventSystem restore and the
    /// focused-selection commit). Deactivates immediately when no animators are assigned. No-op if
    /// already inactive.
    /// </summary>
    public void CloseAnimated()
    {
        if (!gameObject.activeSelf)
            return;

        OnClosing();
        UIPanelGroup.Hide(PanelAnimators, () => gameObject.SetActive(false));
    }

    /// <summary>
    /// Hook invoked the instant a close begins (before the exit animation). Override to cancel any
    /// in-flight effects that must stop exactly when the player leaves (not when the slide-out ends).
    /// </summary>
    protected virtual void OnClosing()
    {
    }

    // Hover (PC pointer): highlight only, no detail change

    public virtual void HoverItem(int index)
    {
        if (index < 0 || index >= GetItemsCount())
            return;

        ListView.SetHovered(index);
    }

    public virtual void UnhoverItem(int index)
    {
        ListView.SetHovered(-1);
    }

    // Focus (click / navigation): highlight + show details

    public virtual void FocusItem(int index)
    {
        if (index < 0 || index >= GetItemsCount())
            return;

        if (index == _focusedItemIndex)
            return;

        _focusedItemIndex = index;
        ListView.SetHovered(-1);
        ListView.SetFocused(index);

        // Focusing an item also previews/stages it; committing happens on BackToLobby().
        SelectItem(index);
    }

    public virtual void SelectItem(int index)
    {
        if (index < 0 || index >= GetItemsCount())
            return;

        _selectedItemIndex = index;

        TData data = GetDataAtIndex(index);
        RefreshDetailView(data, index);
    }

    // Commit (focus the purchase button)

    /// <summary>
    /// Confirm on the focused item. If it can be purchased now, focuses the purchase button so the
    /// next confirm buys it. If it is already owned, equips it and leaves. Otherwise plays the
    /// rejected feedback hook.
    /// </summary>
    public virtual void ConfirmFocused()
    {
        if (_focusedItemIndex < 0)
            return;

        if (_isSelectedItemUnlocked)
        {
            ConfirmUnlockedItem(_focusedItemIndex);
            return;
        }

        if (CanAffordSelected(_focusedItemIndex))
            CommitItem(_focusedItemIndex);
        else
            OnConfirmRejected(_focusedItemIndex);
    }

    public virtual void CommitItem(int index)
    {
        if (index < 0 || index >= GetItemsCount())
            return;

        _committedItemIndex = index;
        _focusedItemIndex = index;

        ListView.SetHovered(-1);
        ListView.SetFocused(index);
        ListView.SetSelected(index);

        SelectItem(index);
        SetPurchaseFocused(true);
    }

    /// <summary>Releases the committed purchase focus and returns control to list navigation.</summary>
    public virtual void UncommitItem()
    {
        if (_committedItemIndex < 0)
            return;

        int previous = _committedItemIndex;
        _committedItemIndex = -1;

        ListView.SetSelected(-1);
        SetPurchaseFocused(false);

        _focusedItemIndex = -1;
        FocusItem(previous);
    }

    public virtual void Next()
    {
        UncommitItem();
        FocusItem((_focusedItemIndex + 1) % GetItemsCount());
    }

    public virtual void Previous()
    {
        UncommitItem();
        FocusItem((_focusedItemIndex - 1 + GetItemsCount()) % GetItemsCount());
    }

    public virtual void BackToLobby()
    {
        // Just leave — the focused selection is committed in HandleShopClosed() (OnDisable),
        // so it applies no matter how the player exits (Back button, Esc, or state change).
        GameManager.Instance.ChangeState(EGameState.Lobby);
    }

    /// <summary>Derived shops commit their focused selection here (no-op if locked/none/unchanged).</summary>
    protected virtual void CommitFocusedSelection()
    {
    }

    /// <summary>Whether the player can currently afford the item at <paramref name="index"/>.</summary>
    protected virtual bool CanAffordSelected(int index) => false;

    /// <summary>Buy the item at <paramref name="index"/> (controller confirm while committed).</summary>
    protected virtual void ExecutePurchase(int index)
    {
    }

    /// <summary>Confirm an already-owned item (equip it / choose it and leave).</summary>
    protected virtual void ConfirmUnlockedItem(int index)
    {
    }

    /// <summary>Feedback hook when confirming an item that cannot be purchased (e.g. too expensive).</summary>
    protected virtual void OnConfirmRejected(int index)
    {
    }

    // Navigation

    /// <summary>Wire Esc / gamepad-B (UI.Cancel), navigation and the Interact confirm while open.</summary>
    protected void EnableShopInput()
    {
        if (_shopInputs == null)
            _shopInputs = new GameInputs();

        _shopInputs.UI.Navigate.performed += OnNavigateInput;
        _shopInputs.UI.Cancel.performed += OnCancelInput;
        _shopInputs.UI.Enable();

        // Confirm uses the Interact action (E / Enter / gamepad South)
        _shopInputs.Player.Interact.performed += OnConfirmInput;
        _shopInputs.Player.Interact.Enable();
    }

    protected void DisableShopInput()
    {
        if (_shopInputs == null)
            return;

        _shopInputs.UI.Navigate.performed -= OnNavigateInput;
        _shopInputs.UI.Cancel.performed -= OnCancelInput;
        _shopInputs.UI.Disable();

        _shopInputs.Player.Interact.performed -= OnConfirmInput;
        _shopInputs.Player.Interact.Disable();
    }

    /// <summary>
    /// Called from derived OnDisable when the shop closes. Disables input, restores the EventSystem
    /// routing and commits the focused selection so it is applied no matter how the player left.
    /// </summary>
    protected void HandleShopClosed()
    {
        DisableShopInput();
        SetPurchaseFocused(false);

        if (EventSystem.current != null)
            EventSystem.current.sendNavigationEvents = _prevSendNavEvents;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        _entityManager = world.EntityManager;
        CommitFocusedSelection();
    }

    private bool IsShopActive() =>
        GameManager.Instance != null && GameManager.Instance.GetGameState() == ShopState;

    private void OnNavigateInput(InputAction.CallbackContext ctx)
    {
        if (!IsShopActive())
            return;

        int count = GetItemsCount();
        if (count == 0)
            return;

        Vector2 v = ctx.ReadValue<Vector2>();

        // Edge-triggered: one step per stick push / key press (release near center to step again).
        if (v.sqrMagnitude < NavDeadzone * NavDeadzone)
        {
            _navAxisActive = false;
            return;
        }

        if (_navAxisActive)
            return;
        _navAxisActive = true;

        // Navigating releases a committed purchase focus and moves to another item.
        if (_committedItemIndex >= 0)
        {
            _committedItemIndex = -1;
            ListView.SetSelected(-1);
            SetPurchaseFocused(false);
        }

        int cols = Mathf.Max(1, Columns);
        int start = _focusedItemIndex < 0 ? 0 : _focusedItemIndex;
        int next;

        if (Mathf.Abs(v.x) >= Mathf.Abs(v.y))
            next = start + (v.x > 0 ? 1 : -1); // horizontal
        else
            next = start + (v.y < 0 ? cols : -cols); // vertical (stick up = previous row)

        next = ((next % count) + count) % count;

        _focusedItemIndex = -1; // force FocusItem to apply
        FocusItem(next);
    }

    private void OnConfirmInput(InputAction.CallbackContext ctx)
    {
        if (!IsShopActive())
            return;

        if (_committedItemIndex >= 0)
            ExecutePurchase(_committedItemIndex); // already focused on the purchase button → buy
        else
            ConfirmFocused(); // focus the purchase button (or equip / reject)
    }

    private void OnCancelInput(InputAction.CallbackContext ctx)
    {
        if (!IsShopActive())
            return;

        if (_committedItemIndex >= 0)
            UncommitItem(); // remove the purchase focus, back to list navigation
        else
            BackToLobby(); // nothing committed → leave
    }

    protected virtual void RefreshActionButton()
    {
        if (ActionButton == null)
            return;

        // The purchase button is shown only when the focused item is locked AND affordable.
        // Unlocked items are chosen/equipped via BackToLobby(), not this button.
        bool showPurchase = !_isSelectedItemUnlocked && CanAffordSelected(_selectedItemIndex);
        ActionButton.gameObject.SetActive(showPurchase);

        if (showPurchase)
        {
            ActionButtonText.text = PurchaseText;
            ActionButtonText.color = PurchaseColor;
        }
        else
        {
            // The purchase focus is meaningless without a visible button.
            SetPurchaseFocused(false);
        }
    }

    /// <summary>
    /// Highlights the purchase button when an item is committed via controller. Uses an optional
    /// frame plus a subtle scale so it works even without a wired highlight. No-op while the button
    /// is hidden.
    /// </summary>
    protected void SetPurchaseFocused(bool focused)
    {
        if (ActionButton == null)
            return;

        bool canShow = focused && ActionButton.gameObject.activeSelf;

        if (PurchaseFocusHighlight != null)
            PurchaseFocusHighlight.SetActive(canShow);

        if (_purchaseFocusTween.isAlive)
            _purchaseFocusTween.Stop();

        float targetScale = canShow ? PurchaseFocusScale : 1f;

        // Only animate when the button is actually visible. Tweening an inactive target warns in
        // PrimeTween (and would be invisible anyway) — this runs on open (button hidden) and on close
        // (parent deactivating), so set the scale instantly in those cases.
        if (ActionButton.gameObject.activeInHierarchy)
        {
            _purchaseFocusTween = Tween.Scale(
                ActionButton.transform,
                targetScale,
                CpUISettings.HoverDuration,
                CpUISettings.HoverEase,
                useUnscaledTime: true);
        }
        else
        {
            ActionButton.transform.localScale = Vector3.one * targetScale;
        }
    }

    protected abstract int GetItemsCount();
    protected abstract TData GetDataAtIndex(int index);
    protected abstract void RefreshListView();
    protected abstract void RefreshDetailView(TData data, int index);
    protected abstract int GetStartingIndex();
}