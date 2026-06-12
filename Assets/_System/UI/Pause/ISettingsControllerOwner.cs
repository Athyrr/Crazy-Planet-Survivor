/// <summary>
/// Implemented by any menu (pause menu, main menu, …) that opens the shared
/// <see cref="SettingsPanelController"/> as a sub-page.
/// </summary>
public interface ISettingsControllerOwner
{
    void OnSettingsClosed();
}
