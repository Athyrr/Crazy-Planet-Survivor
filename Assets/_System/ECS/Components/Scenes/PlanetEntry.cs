using _System.Settings;
using Unity.Entities;
using Unity.Entities.Serialization;

/// <summary>
/// One planet of the baked planets database (PlanetsDatabaseAuthoring), at its database index (PlanetsDatabaseSO
/// order). Only what the ECS reads: the scene to stream and the camera override. FX, foliage and the visual prefab
/// stay on the managed PlanetSO.
/// </summary>
public struct PlanetEntry : IBufferElementData
{
    public EntitySceneReference Scene;

    /// <summary>False: the planet uses the global camera (CpBaseCameraSettings.Default, read live).</summary>
    public bool OverrideCamera;

    public CpBaseCameraSettings.CameraSettingsData Camera;
}
