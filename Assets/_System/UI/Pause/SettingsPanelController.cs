using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Basic in-run settings panel opened from the pause menu's Options button. Three volume rows
/// (Master / Music / SFX) plus Back, reusing the pause menu's button style. Left/Right (or click)
/// adjusts the focused volume; Up/Down moves between rows; Cancel / Back returns to the pause menu.
///
/// Master volume drives <see cref="AudioListener.volume"/> immediately via <see cref="GameSettings"/>;
/// Music and SFX are persisted for a future audio backend. Navigation uses the same explicit-input
/// model as the pause menu and shops (EventSystem nav/submit routing stays disabled by the owner).
/// </summary>
public class SettingsPanelController : UIControllerBase
{
    private enum Row { Master, Music, Sfx, Back }

    [Header("Rows (top to bottom)")]
    public Button MasterRow;
    public Button MusicRow;
    public Button SfxRow;
    public Button BackRow;

    [Tooltip("Volume change applied per Left/Right step or click on a volume row.")]
    [Range(0.05f, 0.5f)]
    public float VolumeStep = 0.1f;

    [Header("Focus tint (label color)")]
    public Color NormalColor = new Color(0.6528f, 0.6528f, 0.6528f, 1f);
    public Color HighlightColor = new Color(0.2934f, 0.9094f, 0.7754f, 1f);

    private PauseUIController _owner;
    private GameInputs _inputs;
    private Button[] _rows;
    private int _focusedIndex = -1;
    private bool _navAxisActive;

    private const float NavDeadzone = 0.5f;

    private void Awake() => EnsureRows();

    /// <summary>Opens the panel as a page of the pause menu; returns to it via Back/Cancel.</summary>
    public void Open(PauseUIController owner)
    {
        _owner = owner;
        gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        EnsureRows();
        WireRows(true);
        EnableInput();
        RefreshLabels();

        _focusedIndex = -1;
        _navAxisActive = false;
        FocusRow(0);
    }

    private void OnDisable()
    {
        WireRows(false);
        DisableInput();
    }

    public void Close()
    {
        gameObject.SetActive(false); // triggers OnDisable -> input torn down

        if (_owner != null)
            _owner.OnSettingsClosed();
    }

    // Row wiring (mouse click + gamepad submit share the same per-row action)

    private void EnsureRows()
    {
        if (_rows == null)
            _rows = new[] { MasterRow, MusicRow, SfxRow, BackRow };
    }

    private void WireRows(bool wire)
    {
        SetListener(MasterRow, () => StepVolume(Row.Master, +1, wrap: true), wire);
        SetListener(MusicRow, () => StepVolume(Row.Music, +1, wrap: true), wire);
        SetListener(SfxRow, () => StepVolume(Row.Sfx, +1, wrap: true), wire);
        SetListener(BackRow, Close, wire);
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

    // Volume

    private void StepVolume(Row row, int direction, bool wrap)
    {
        float current = ReadVolume(row);
        float next = current + direction * VolumeStep;
        next = Mathf.Round(next * 100f) / 100f;

        if (wrap)
            next = next > 1.0001f ? 0f : (next < -0.0001f ? 1f : next);
        else
            next = Mathf.Clamp01(next);

        WriteVolume(row, next);
        RefreshLabels();
    }

    private static float ReadVolume(Row row) => row switch
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
            case Row.Master: GameSettings.MasterVolume = value; break;
            case Row.Music: GameSettings.MusicVolume = value; break;
            case Row.Sfx: GameSettings.SfxVolume = value; break;
        }
    }

    private void RefreshLabels()
    {
        SetLabel(MasterRow, "Master", GameSettings.MasterVolume);
        SetLabel(MusicRow, "Music", GameSettings.MusicVolume);
        SetLabel(SfxRow, "SFX", GameSettings.SfxVolume);
        SetText(BackRow, "Back");
    }

    private static void SetLabel(Button row, string label, float volume01)
    {
        SetText(row, $"{label}   {Mathf.RoundToInt(volume01 * 100f)}%");
    }

    private static void SetText(Button row, string text)
    {
        if (row == null)
            return;

        var tmp = row.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
            tmp.text = text;
    }

    // Input

    private void EnableInput()
    {
        _inputs ??= new GameInputs();

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
        Vector2 v = ctx.ReadValue<Vector2>();

        if (v.sqrMagnitude < NavDeadzone * NavDeadzone)
        {
            _navAxisActive = false;
            return;
        }

        if (_navAxisActive)
            return;
        _navAxisActive = true;

        if (Mathf.Abs(v.y) >= Mathf.Abs(v.x))
            StepFocus(v.y > 0f ? -1 : 1); // vertical: change row
        else if (_focusedIndex >= 0 && _focusedIndex < (int)Row.Back)
            StepVolume((Row)_focusedIndex, v.x > 0f ? 1 : -1, wrap: false); // horizontal: adjust volume
    }

    private void OnSubmit(InputAction.CallbackContext ctx)
    {
        if (_focusedIndex < 0 || _focusedIndex >= _rows.Length)
            return;

        var row = _rows[_focusedIndex];
        if (row != null && row.interactable)
            row.onClick.Invoke();
    }

    private void OnCancel(InputAction.CallbackContext ctx) => Close();

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

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(row.gameObject);

        ApplyRowColors();
    }

    // The settings rows are built without a Button target graphic, so we tint the labels ourselves
    // for focus feedback instead of relying on the Button's color transition.
    private void ApplyRowColors()
    {
        for (int i = 0; i < _rows.Length; i++)
        {
            var tmp = _rows[i] != null ? _rows[i].GetComponentInChildren<TMP_Text>(true) : null;
            if (tmp != null)
                tmp.color = i == _focusedIndex ? HighlightColor : NormalColor;
        }
    }
}
