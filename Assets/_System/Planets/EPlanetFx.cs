/// <summary>
/// Ambient FX of a planet (<see cref="PlanetSO.Fx"/>), enabled by FxSelectorRenderer when the planet is loaded.
/// Serialized as an int: None, Wind, Lava keep the values of the former FxSelectorRenderer.EFxSelectorRenderer
/// (NONE, WIND, LAVA) and must never be reordered.
/// </summary>
public enum EPlanetFx
{
    None = 0,
    Wind = 1,
    Lava = 2,
}
