using UnityEngine;

public class SaveManagerExtender : MonoBehaviour
{
    // The auto-save on lobby arrival is intentionally disabled. It never fired before so-inheritance lot 3: the lobby
    // baked the None planet id, so the Lobby branch was never reached. Once the loader gave the real identity it would
    // have saved on every lobby arrival. The save system is a separate need: do not re-enable it here.

    private void OnEnable()
    {
        LoadLastSave();
    }

    private void LoadLastSave()
    {
        SaveManager.LoadSelectedSave();
    }
}
