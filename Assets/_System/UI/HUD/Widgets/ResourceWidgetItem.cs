using _System.ECS.Authorings.Resources;
using TMPro;
using UnityEngine;
using Unity.Entities;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class ResourceWidgetItem : UIViewItemBase
{
    [SerializeField] private TMP_Text _resourceCountText;
    [SerializeField] private Image _resourceImage;

    private EResourceType _resourceType;

    private EntityManager _entityManager;
    private EntityQuery _sourceQuery;
    private bool _init;
    private bool _ecsContext;

    /// <summary>
    /// Configures this widget to track a resource type for the HUD (reads from Player entity).
    /// </summary>
    public void Refresh(EResourceType resourceType, Sprite resourceImageTexture, Color iconColor, int defaultValue = -1)
    {
        _resourceType = resourceType;
        _resourceImage.sprite = resourceImageTexture;
        _resourceImage.color = iconColor;

        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _sourceQuery = default;

        if (defaultValue >= 0)
        {
            _resourceCountText.text = "" + defaultValue;
            _ecsContext = false;
        }
        else
        {
            _sourceQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Player>());
            _ecsContext = true;
        }

        _init = true;
    }

    /// <summary>
    /// Configures this widget to track a resource type for a shop (reads from GameState entity).
    /// </summary>
    public void RefreshMeta(EResourceType resourceType, Sprite resourceImageTexture, Color iconColor)
    {
        _resourceType = resourceType;
        _resourceImage.sprite = resourceImageTexture;
        _resourceImage.color = iconColor;

        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _sourceQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<GameState>());
        _ecsContext = true;
        _init = true;
    }

    private void Update()
    {
        if (!_init || !_ecsContext)
            return;

        // Refresh entity manager if the world was re-created (scene reload, domain restart)
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        _entityManager = world.EntityManager;

        if (_sourceQuery.IsEmpty)
            return;

        var sourceEntity = _sourceQuery.GetSingletonEntity();
        if (!_entityManager.HasBuffer<ResourceBufferElement>(sourceEntity))
            return;

        var resources = _entityManager.GetBuffer<ResourceBufferElement>(sourceEntity);
        _resourceCountText.text = $"{resources.GetAmount(_resourceType)}";
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
    }
}