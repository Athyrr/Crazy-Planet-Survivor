using System.Collections.Generic;
using EnhancedFramework.Core.Settings;
using RuntimeSettings;
using UnityEngine;

[CreateAssetMenu(fileName = "GameplaySettings", menuName = "Settings/Gameplay")]
public class GameplaySettings : BaseSettings<GameplaySettings>
{
    [Tooltip("Mimic legs computation method (0 = DeCasteljau, 1 = Bernstein).")]
    public int mimicLegMethod;
    
    [Tooltip("Skips the Onboarding tutorial.")]
    public bool skipOnboarding;
    
    [Tooltip("Board Mode. 0 = Path1, 1 = Path4.")]
    public int boardModeIndex;

    public bool useGameTimer = false;
    
    public bool requireAllPlayerWinToEndGame = true;
    
    // public bool allowUnsitOutsideMJTimes = true;
    public bool noGameOver;
    public bool eventNoInvasiveTags;
    public bool eventNoFlow;

    public string boardConfiguration;
    
    [Header("Resume Settings")]
    public string resumeSettingsPreset;
    
    [Header("Debug")]
    public bool bigPawnStartsEvents;
    public bool infiniteStamina;
    public bool infiniteHealth;
    public bool restrictBoardInteract;
    public float monsterVelocity = 1;
    public string throwType;
    public bool scrollRotateInvItem;
    public float rotateInvItemSpeed = 1;
    public bool forceDiceQueue;
    public float nicknameDistance;

    [Header("Monster Despawn")]
    public List<float> zoneDespawnProtection;
    public float monsterDeathTimeFactor = 1.5f;
    public bool deadPostProcess = true;
    public bool spectatorMode;
    public bool stealth;
    public bool noMicNoise;
    public bool monsterFigurines = true;
    public bool slotNumbers = true;
    public float cameraSnapping;
    
    [Header("Idol")]
    public float sellDistance = 3;
    
    public override void Init()
    {
        base.Init();
        SettingsManager.SettingChangedEvent -= OnSettingChanged;
        SettingsManager.SettingChangedEvent += OnSettingChanged;
    }

    private void OnSettingChanged(SettingsManager.SettingChanged changed)
    {
        // if (changed.FullKey == "Gameplay.cameraSensitivity")
        //     SettingsManager.SaveSettings();
    }
}
