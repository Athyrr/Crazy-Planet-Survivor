using UnityEngine;

/// <summary>
/// Simple MonoBehaviour that syncs meta-progression data from the manager
/// into the ECS buffer when the Lobby scene starts.
/// This ensures ApplyMetaProgressionSystem can read the buffer at run start
/// even if the player never opened the meta-progression shop UI.
/// </summary>
public class MetaProgressionBootstrap : MonoBehaviour
{
    [SerializeField] private MetaUpgradesDatabaseSO _database;

    private void Start()
    {
        if (_database == null)
        {
            Debug.LogWarning("[MetaProgressionBootstrap] No database assigned.");
            return;
        }

        MetaProgressionManager.LoadFromSave();
        MetaProgressionManager.SyncToBuffer(_database);
    }
}
