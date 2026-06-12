using System;
using _System.Settings;
using UnityEngine;

/// <summary>
/// Lightweight player-facing settings persisted in PlayerPrefs (separate from the design-time
/// <see cref="CpSettings{T}"/> assets). Currently holds the three audio
/// volumes used by the pause-menu Settings panel.
///
/// <para><b>Master</b> is applied immediately to <see cref="AudioListener.volume"/> (and re-applied at
/// startup). <b>Music</b> and <b>SFX</b> are persisted and exposed via <see cref="Changed"/> so a future
/// audio backend (Wwise / AudioMixer) can read and react to them without changing this API.</para>
/// </summary>
public static class GameSettings
{
    public static event Action Changed;

    private const string MasterKey = "settings.volume.master";
    private const string MusicKey = "settings.volume.music";
    private const string SfxKey = "settings.volume.sfx";

    private static float _master = -1f;
    private static float _music = -1f;
    private static float _sfx = -1f;

    public static float MasterVolume
    {
        get => Get(ref _master, MasterKey);
        set => Set(ref _master, MasterKey, value);
    }

    public static float MusicVolume
    {
        get => Get(ref _music, MusicKey);
        set => Set(ref _music, MusicKey, value);
    }

    public static float SfxVolume
    {
        get => Get(ref _sfx, SfxKey);
        set => Set(ref _sfx, SfxKey, value);
    }

    /// <summary>Pushes the current values to the actual audio output. Called on every change and once at startup.</summary>
    public static void Apply()
    {
        AudioListener.volume = MasterVolume;
        // Music / SFX have no backend yet — consumers listen to Changed and read the properties.
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyOnStartup() => Apply();

    private static float Get(ref float cache, string key)
    {
        if (cache < 0f)
            cache = Mathf.Clamp01(PlayerPrefs.GetFloat(key, 1f));
        return cache;
    }

    private static void Set(ref float cache, string key, float value)
    {
        value = Mathf.Clamp01(value);
        if (Mathf.Approximately(cache, value))
            return;

        cache = value;
        PlayerPrefs.SetFloat(key, value);
        PlayerPrefs.Save();

        Apply();
        Changed?.Invoke();
    }
}
