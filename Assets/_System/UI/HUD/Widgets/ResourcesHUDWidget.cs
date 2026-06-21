using _System.Settings;
using UnityEngine;

public class ResourcesHUDWidget : MonoBehaviour
{
    [Header("Reference")] [SerializeField] private ResourceWidgetItem resourceModel;
    [SerializeField] private Transform _container;

    [SerializeField] private ResourceDatabaseSO _resourceDatabase;

    private void OnEnable()
    {
        Rebuild();
    }

    /// <summary>
    /// Destroys existing resource items and recreates them from the database.
    /// Each item will track the Player's ResourceBuffer via its Update loop.
    /// Call this at run start to reset all displays to 0.
    /// </summary>
    public void Rebuild()
    {
        // Clear existing items
        foreach (Transform child in _container)
            Destroy(child.gameObject);

        if (_resourceDatabase == null) return;

        foreach (var resource in _resourceDatabase.Resources)
        {
            var instance = Instantiate(resourceModel, _container.transform);
            instance.Refresh(resource.Type, resource.Icon, resource.Color);
        }
    }
}