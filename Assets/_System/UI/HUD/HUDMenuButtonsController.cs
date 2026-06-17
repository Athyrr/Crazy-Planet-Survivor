using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connects the in-run HUD menu buttons (MenuButtons group) to their actions:
/// one toggles the run's pause menu, the other opens/closes the stats tab.
/// </summary>
public class HUDMenuButtonsController : UIControllerBase
{
    [Header("Buttons")]
    public Button PauseButton;
    public Button StatsButton;

    [Header("Targets")]
    [Tooltip("Drives the run's pause state via TogglePause(). Auto-found if left unassigned.")]
    public RunManager RunManager;

    [Tooltip("Owns the stats tab view. Auto-found if left unassigned.")]
    public TabStatsUIController StatsController;

    private void Awake()
    {
        if (RunManager == null)
            RunManager = FindAnyObjectByType<RunManager>(FindObjectsInactive.Include);
        if (StatsController == null)
            StatsController = FindAnyObjectByType<TabStatsUIController>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        if (PauseButton != null)
            PauseButton.onClick.AddListener(TogglePause);
        if (StatsButton != null)
            StatsButton.onClick.AddListener(ToggleStats);
    }

    private void OnDisable()
    {
        if (PauseButton != null)
            PauseButton.onClick.RemoveListener(TogglePause);
        if (StatsButton != null)
            StatsButton.onClick.RemoveListener(ToggleStats);
    }

    private void TogglePause()
    {
        if (RunManager != null)
            RunManager.TogglePause();
    }

    private void ToggleStats()
    {
        if (StatsController != null)
            StatsController.Toggle();
    }
}
