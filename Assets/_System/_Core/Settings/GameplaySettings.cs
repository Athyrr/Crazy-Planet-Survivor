using EnhancedFramework.Core.Settings;
using RuntimeSettings;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "GameplaySettings", menuName = "Settings/Gameplay")]
public class GameplaySettings : BaseSettings<GameplaySettings>
{
    [FormerlySerializedAs("profiler")] public bool enableProfiler;
    
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
