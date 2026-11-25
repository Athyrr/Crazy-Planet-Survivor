using _System.Settings;
using Unity.Entities.Serialization;
using UnityEngine;

/// <summary>
/// Definition of one planet (a single body): the only place a planet is described. Adding a planet = one PlanetSO,
/// its scene, and an entry in the planets database (PlanetsDatabaseSO). Carries no id: a planet's runtime key is its
/// index in the baked database. Nothing here is baked except Scene and the camera override (PlanetsDatabaseAuthoring);
/// FX and foliage are read on the managed side through GameManager.OnPlanetSelected.
/// </summary>
[CreateAssetMenu(fileName = "Planet_New", menuName = "Survivor/Planets/Planet")]
public class PlanetSO : EntityDefinitionSO
{
    [Header("Scene")]
    [Tooltip("Entity scene where the planet is played: the whole scene (playable planet, scenery, visual copies of " +
             "other bodies). Streamed by SceneLoadingSystem through the planets database.")]
    public EntitySceneReference Scene;

    [Header("Visual")]
    [Tooltip("Non-playable visual copy, for any representation outside the run. Written by the WFC tool for WFC " +
             "planets; empty for Lobby and Sandbox. Must never carry a PlanetDataAuthoring.")]
    public GameObject VisualPrefab;

    [Header("Ambience")]
    [Tooltip("Ambient FX enabled by FxSelectorRenderer while this planet is loaded.")]
    public EPlanetFx Fx = EPlanetFx.None;

    [Tooltip("Foliage drawn by FoliageRenderer while this planet is loaded. Empty = no foliage.")]
    public FoliageData Foliage;

    [Header("Camera")]
    [Tooltip("Unchecked: the planet uses the global camera (CpBaseCameraSettings, read live, tunable in Play). " +
             "Checked: Camera below, baked with the planets database (rebake SC_Entity_Core after editing).")]
    public bool OverrideCamera;

    [Tooltip("Used only when Override Camera is checked.")]
    public CpBaseCameraSettings.CameraSettingsData Camera;

    /// <summary>Camera this planet uses: its override, else the global default.</summary>
    public CpBaseCameraSettings.CameraSettingsData EffectiveCamera => CpBaseCameraSettings.Resolve(OverrideCamera, Camera);
}
