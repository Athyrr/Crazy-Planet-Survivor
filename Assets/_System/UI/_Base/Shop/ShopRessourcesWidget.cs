using _System.Settings;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Displays the player's meta resources (persistent currencies) in a shop view.
/// Reads from ResourceBufferElement on the GameState entity.
/// Place this widget in each shop scene/prefab.
/// </summary>
public class ShopRessourcesWidget : MonoBehaviour
{
    [FormerlySerializedAs("_ressourceItemPrefab")]
    [Header("References")]
    [SerializeField] private ResourceWidgetItem resourceItemPrefab;
    [SerializeField] private Transform _container;

    [Header("Database")]
    [SerializeField] private ResourceDatabaseSO _resourceDatabase;

    private EntityManager _entityManager;
    private EntityQuery _gameStateQuery;
    private bool _initialized;

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
    }

    private void Start()
    {
        SpawnResourceDisplays();
        Refresh();
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void SpawnResourceDisplays()
    {
        if (resourceItemPrefab == null || _container == null) return;
        if (_resourceDatabase == null) return;

        // Clear existing items
        foreach (Transform child in _container)
            Destroy(child.gameObject);

        // Create one display per resource type from the database
        foreach (var resource in _resourceDatabase.Resources)
        {
            var instance = Instantiate(resourceItemPrefab, _container);
            instance.RefreshMeta(resource.Type, resource.Icon, resource.Color);
        }

        _initialized = true;
    }

    /// <summary>
    /// Refresh all resource displays to reflect current GameState buffer values.
    /// Call this after a purchase to update the UI.
    /// </summary>
    public void Refresh()
    {
        if (!_initialized) return;
        if (_gameStateQuery.IsEmpty) return;

        // Individual resource items already update themselves via ECS queries
        // RessourceWidgetItem uses Update() to refresh, so just enable them.
    }
}
