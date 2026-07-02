using TMPro;
using UnityEngine;

/// <summary>
/// Chronomètre de run : track le temps écoulé depuis le début de la partie.
/// Se met en pause avec le game state (Paused, UpgradeSelection, GameOver)
/// et se réinitialise au retour au Lobby.
///
/// À attacher sur le GameObject "Chrono" dans la scène.
/// Référencer un TMP_Text dans <see cref="TimeDisplay"/> pour l'affichage.
/// </summary>
public class ChronoController : MonoBehaviour
{
    [Header("Display")]
    [Tooltip("Texte où afficher le temps écoulé (mm:ss ou h:mm:ss).")]
    public TMP_Text TimeDisplay;

    [Header("Settings")]
    [SerializeField]
    [Tooltip("Format du temps quand il est < 1h. Defaut : mm:ss")]
    private string _timeFormat = @"mm\:ss";

    private float _elapsedTime;
    private bool _isRunning;

    // ──────────────────────────────────────────────
    //  Unity lifecycle
    // ──────────────────────────────────────────────

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStateChanged += HandleGameState;
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStateChanged -= HandleGameState;
    }

    private void Update()
    {
        if (!_isRunning)
            return;

        _elapsedTime += Time.deltaTime;
        UpdateDisplay();
    }

    // ──────────────────────────────────────────────
    //  Game state
    // ──────────────────────────────────────────────

    private void HandleGameState(EGameState newState)
    {
        switch (newState)
        {
            case EGameState.Running:
                _isRunning = true;
                break;

            case EGameState.Paused:
            case EGameState.UpgradeSelection:
            case EGameState.GameOver:
                _isRunning = false;
                break;

            case EGameState.Lobby:
                _isRunning = false;
                _elapsedTime = 0f;
                UpdateDisplay();
                break;
        }
    }

    // ──────────────────────────────────────────────
    //  Display
    // ──────────────────────────────────────────────

    private void UpdateDisplay()
    {
        if (TimeDisplay != null)
            TimeDisplay.text = FormatTime(_elapsedTime);
    }

    private string FormatTime(float seconds)
    {
        var span = System.TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1f
            ? span.ToString(@"h\:mm\:ss")
            : span.ToString(_timeFormat);
    }

    // ──────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────

    /// <summary>Remet le chrono à zéro et rafraîchit l'affichage.</summary>
    public void ResetTimer()
    {
        _elapsedTime = 0f;
        UpdateDisplay();
    }

    /// <summary>Retourne le temps écoulé en secondes.</summary>
    public float GetElapsedSeconds() => _elapsedTime;

    /// <summary>Retourne le temps formaté (mm:ss ou h:mm:ss).</summary>
    public string GetFormattedTime() => FormatTime(_elapsedTime);
}
