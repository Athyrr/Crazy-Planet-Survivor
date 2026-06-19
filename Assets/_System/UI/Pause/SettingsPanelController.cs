using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Settings panel opened from a menu's Options button. Three volume rows (Master / Music / SFX) plus
/// Back. Two-step, controller-friendly model: Up/Down browses rows (the name label tints); Submit on a
/// volume row enters edit mode (its value bolds and its slider becomes the active control); then
/// Left/Right moves the slider; Submit / Cancel leaves edit mode. Mouse and touch can drag a slider
/// directly at any time (mobile friendly). Cancel in browse closes the panel.
///
/// Master volume drives <see cref="AudioListener.volume"/> immediately via <see cref="GameSettings"/>;
/// Music and SFX are persisted for a future audio backend. Navigation uses the same explicit-input
/// model as the pause menu and shops (EventSystem nav/submit routing stays disabled by the owner).
/// </summary>
public class SettingsPanelController : UIControllerBase
{
    private enum Row
    {
        Master,
        Music,
        Sfx,
        Back,
    }

    [Header("Rows (top to bottom)")]
    [Tooltip(
        "Focusable Button of each row. May live on the row object itself or on a child label "
            + "(e.g. a layout-group row split into a Label + Value + Slider)."
    )]
    public Button MasterRow;
    public Button MusicRow;
    public Button SfxRow;
    public Button BackRow;

    [Header("Volume sliders")]
    [Tooltip(
        "Slider (min 0, max 1) that edits each volume row. Drag works for mouse/touch; the "
            + "controller moves it with Left/Right while the row is in edit mode."
    )]
    public Slider MasterSlider;
    public Slider MusicSlider;
    public Slider SfxSlider;

    [Header("Volume value labels (optional)")]
    [Tooltip(
        "TMP showing the percentage for each volume row. Bolds while its row is being edited; "
            + "its color is never changed."
    )]
    public TMP_Text MasterValue;
    public TMP_Text MusicValue;
    public TMP_Text SfxValue;

    [Tooltip("Volume change applied per Left/Right step while editing a volume row.")]
    [Range(0.01f, 0.5f)]
    public float VolumeStep = 0.1f;

    private ISettingsControllerOwner _owner;
    private GameInputs _inputs;
    private Button[] _rows;
    private int _focusedIndex = -1;
    private int _editingIndex = -1; // -1 = browsing; otherwise the volume row being edited
    private NavRepeatFilter _nav;

    private void Awake() => EnsureRows();

    /// <summary>Opens the panel as a page of the owning menu; returns to it via Back/Cancel.</summary>
    public void Open(ISettingsControllerOwner owner)
    {
        _owner = owner;
        gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        EnsureRows();
        ApplyLabelStyles();
        WireRows(true);
        EnableInput();
        InitRows();

        _focusedIndex = -1;
        _editingIndex = -1;
        _nav.Reset();
        FocusRow(0);
    }

    private void OnDisable()
    {
        WireRows(false);
        DisableInput();
        ExitEdit();
    }

    public void Close()
    {
        gameObject.SetActive(false); // triggers OnDisable -> input torn down

        if (_owner != null)
            _owner.OnSettingsClosed();
    }

    // Setup

    private void EnsureRows()
    {
        if (_rows == null)
            _rows = new[] { MasterRow, MusicRow, SfxRow, BackRow };
    }

    // Each row tints its name label from the shared CpUISettings palette (idle grey -> blue on
    // hover/focus) instead of a background image. The value keeps its own color; its edit emphasis
    // is handled by bolding (see RefreshValueEmphasis), not tinting.
    private void ApplyLabelStyles()
    {
        UILabelPalette.ApplyToButton(MasterRow);
        UILabelPalette.ApplyToButton(MusicRow);
        UILabelPalette.ApplyToButton(SfxRow);
        UILabelPalette.ApplyToButton(BackRow);
    }

    // Names are static; the sliders are seeded from the stored volumes (without firing change events),
    // then the percentage labels are synced.
    private void InitRows()
    {
        SetText(BackRow, "Back");

        InitVolumeRow(Row.Master, GameSettings.MasterVolume);
        InitVolumeRow(Row.Music, GameSettings.MusicVolume);
        InitVolumeRow(Row.Sfx, GameSettings.SfxVolume);
    }

    private void InitVolumeRow(Row row, float volume01)
    {
        Slider slider = SliderFor(row);
        if (slider != null)
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(volume01);
        }

        // Split layout: the name is static on the row label (the percent lives on the Value TMP).
        if (ValueFor(row) != null)
            SetText(ButtonFor(row), RowName(row));

        UpdateVolumeValue(row, volume01);
    }

    // Wiring

    private void WireRows(bool wire)
    {
        SetListener(MasterRow, () => SelectVolumeRow(Row.Master), wire);
        SetListener(MusicRow, () => SelectVolumeRow(Row.Music), wire);
        SetListener(SfxRow, () => SelectVolumeRow(Row.Sfx), wire);
        SetListener(BackRow, Close, wire);

        WireSlider(MasterSlider, Row.Master, wire);
        WireSlider(MusicSlider, Row.Music, wire);
        WireSlider(SfxSlider, Row.Sfx, wire);
    }

    // A click / tap on a volume row's button enters edit mode (mouse users can then drag its slider).
    private void SelectVolumeRow(Row row)
    {
        FocusRow((int)row);
        EnterEdit((int)row);
    }

    private static void SetListener(Button button, UnityEngine.Events.UnityAction action, bool wire)
    {
        if (button == null)
            return;

        // Per-row lambdas can't be removed individually, so clear all and (when wiring) re-add.
        button.onClick.RemoveAllListeners();
        if (wire)
            button.onClick.AddListener(action);
    }

    private void WireSlider(Slider slider, Row row, bool wire)
    {
        if (slider == null)
        {
            Debug.LogWarning(
                $"A Slider has been defined for Row {row} but none are set in the Settings Panel Component"
            );
            return;
        }

        slider.onValueChanged.RemoveAllListeners();
        if (wire)
            slider.onValueChanged.AddListener(value => OnSliderChanged(row, value));
    }

    // Volume

    // Controller Left/Right while editing: nudge the slider, which writes the volume through OnSliderChanged.
    private void AdjustVolume(Row row, int direction)
    {
        Slider slider = SliderFor(row);
        float current = slider != null ? slider.value : ReadVolume(row);
        float next = Mathf.Clamp01(Mathf.Round((current + direction * VolumeStep) * 100f) / 100f);

        if (slider != null)
        {
            slider.value = next; // fires OnSliderChanged -> writes volume + label
        }
        else
        {
            WriteVolume(row, next);
            UpdateVolumeValue(row, next);
        }
    }

    // Slider drag (mouse / touch) or programmatic nudge: persist the volume, sync the label, and make
    // this the focused + edited row so the label tints and the value bolds.
    private void OnSliderChanged(Row row, float value)
    {
        WriteVolume(row, value);
        UpdateVolumeValue(row, value);

        int index = (int)row;
        if (_focusedIndex != index)
            FocusRow(index);
        if (_editingIndex != index)
            EnterEdit(index);
    }

    private static float ReadVolume(Row row) =>
        row switch
        {
            Row.Master => GameSettings.MasterVolume,
            Row.Music => GameSettings.MusicVolume,
            Row.Sfx => GameSettings.SfxVolume,
            _ => 0f,
        };

    private static void WriteVolume(Row row, float value)
    {
        switch (row)
        {
            case Row.Master:
                GameSettings.MasterVolume = value;
                break;
            case Row.Music:
                GameSettings.MusicVolume = value;
                break;
            case Row.Sfx:
                GameSettings.SfxVolume = value;
                break;
        }
    }

    // Split layout (separate Value TMP): only the percent is rewritten (cheap, runs on slider drag).
    // Single-label layout (no Value TMP, e.g. the pause menu): "Name   NN%" is rewritten on the label.
    private void UpdateVolumeValue(Row row, float value01)
    {
        int percent = Mathf.RoundToInt(value01 * 100f);
        TMP_Text value = ValueFor(row);

        if (value != null)
            value.text = $"{percent}%";
        else
            SetText(ButtonFor(row), $"{RowName(row)}   {percent}%");
    }

    private static string RowName(Row row) =>
        row switch
        {
            Row.Master => "Master",
            Row.Music => "Music",
            Row.Sfx => "SFX",
            _ => string.Empty,
        };

    private static void SetText(Button row, string text)
    {
        if (row == null)
            return;

        var tmp = row.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
            tmp.text = text;
    }

    private Button ButtonFor(Row row) =>
        row switch
        {
            Row.Master => MasterRow,
            Row.Music => MusicRow,
            Row.Sfx => SfxRow,
            Row.Back => BackRow,
            _ => null,
        };

    private Slider SliderFor(Row row) =>
        row switch
        {
            Row.Master => MasterSlider,
            Row.Music => MusicSlider,
            Row.Sfx => SfxSlider,
            _ => null,
        };

    private TMP_Text ValueFor(Row row) =>
        row switch
        {
            Row.Master => MasterValue,
            Row.Music => MusicValue,
            Row.Sfx => SfxValue,
            _ => null,
        };

    // Input

    private void EnableInput()
    {
        _inputs ??= new GameInputs();

        // Remove before add so a second EnableInput can't double-subscribe the handlers — a doubled
        // OnNavigate steps focus twice per push.
        _inputs.UI.Navigate.performed -= OnNavigate;
        _inputs.UI.Submit.performed -= OnSubmit;
        _inputs.UI.Cancel.performed -= OnCancel;

        _inputs.UI.Navigate.performed += OnNavigate;
        _inputs.UI.Submit.performed += OnSubmit;
        _inputs.UI.Cancel.performed += OnCancel;
        _inputs.UI.Enable();
    }

    private void DisableInput()
    {
        if (_inputs == null)
            return;

        _inputs.UI.Navigate.performed -= OnNavigate;
        _inputs.UI.Submit.performed -= OnSubmit;
        _inputs.UI.Cancel.performed -= OnCancel;
        _inputs.UI.Disable();
    }

    private void OnNavigate(InputAction.CallbackContext ctx)
    {
        // One discrete step per push (hysteresis filters analog jitter).
        switch (_nav.Evaluate(ctx.ReadValue<Vector2>()))
        {
            // Vertical: leave edit mode (if any) and move between rows.
            case NavDirection.Up:
                ExitEdit();
                StepFocus(-1);
                break;
            case NavDirection.Down:
                ExitEdit();
                StepFocus(1);
                break;
            // Horizontal only edits while a volume row is selected for editing.
            case NavDirection.Left:
                if (_editingIndex >= 0)
                    AdjustVolume((Row)_editingIndex, -1);
                break;
            case NavDirection.Right:
                if (_editingIndex >= 0)
                    AdjustVolume((Row)_editingIndex, 1);
                break;
        }
    }

    private void OnSubmit(InputAction.CallbackContext ctx)
    {
        // While editing, Submit confirms and returns to browsing.
        if (_editingIndex >= 0)
        {
            ExitEdit();
            return;
        }

        if (_focusedIndex < 0 || _focusedIndex >= _rows.Length)
            return;

        var row = _rows[_focusedIndex];
        if (row != null && row.interactable)
            row.onClick.Invoke(); // volume row -> SelectVolumeRow (enter edit); Back -> Close
    }

    // Cancel backs out of edit mode first; in browse mode it closes the panel.
    private void OnCancel(InputAction.CallbackContext ctx)
    {
        if (_editingIndex >= 0)
            ExitEdit();
        else
            Close();
    }

    private void StepFocus(int direction)
    {
        int count = _rows.Length;
        int index = _focusedIndex < 0 ? 0 : _focusedIndex;

        for (int i = 0; i < count; i++)
        {
            index = ((index + direction) % count + count) % count;
            if (_rows[index] != null && _rows[index].interactable)
            {
                FocusRow(index);
                return;
            }
        }
    }

    private void FocusRow(int index)
    {
        if (index < 0 || index >= _rows.Length)
            return;

        var row = _rows[index];
        if (row == null || !row.interactable)
            return;

        _focusedIndex = index;

        // Selection drives the name label's blue tint (browse feedback) via the shared ColorTint.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(row.gameObject);
    }

    // Editing: only volume rows can be edited; their value is bolded while editing.
    private void EnterEdit(int index)
    {
        if (index < 0 || index >= (int)Row.Back)
            return;

        _editingIndex = index;
        RefreshValueEmphasis();
    }

    private void ExitEdit()
    {
        if (_editingIndex < 0)
            return;

        _editingIndex = -1;
        RefreshValueEmphasis();
    }

    // The edited volume row's value is bolded (its color is left untouched); the others are un-bolded.
    private void RefreshValueEmphasis()
    {
        SetBold(MasterValue, _editingIndex == (int)Row.Master);
        SetBold(MusicValue, _editingIndex == (int)Row.Music);
        SetBold(SfxValue, _editingIndex == (int)Row.Sfx);
    }

    private static void SetBold(TMP_Text value, bool bold)
    {
        if (value == null)
            return;

        value.fontStyle = bold
            ? value.fontStyle | FontStyles.Bold
            : value.fontStyle & ~FontStyles.Bold;
    }
}
