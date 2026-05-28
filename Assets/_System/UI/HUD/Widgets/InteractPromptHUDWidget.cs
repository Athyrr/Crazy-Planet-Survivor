using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class InteractPromptHUDWidget : MonoBehaviour
{
    [Header("UI References")] public Button Button;
    public GameObject PromptRoot;

    private EntityManager _entityManager;
    private EntityQuery _inRangeQuery;
    private PlayerController _playerController;
    private bool _initialized;

    private void Awake()
    {
    }

    private void OnEnable()
    {
        EnsureInit();
        if (Button != null)
            Button.onClick.AddListener(OnPressed);

        SetPromptVisible(false);
    }

    private void OnDisable()
    {
        if (Button != null)
            Button.onClick.RemoveListener(OnPressed);
    }

    private void Update()
    {
        if (!_initialized)
        {
            EnsureInit();
            if (!_initialized)
                return;
        }

        SetPromptVisible(!_inRangeQuery.IsEmpty);
    }

    private void EnsureInit()
    {
        if (_initialized)
            return;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        _entityManager = world.EntityManager;
        _inRangeQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<InteractableInRangeTag>());

        if (_playerController == null)
            _playerController = FindFirstObjectByType<PlayerController>();

        _initialized = true;
    }

    private void SetPromptVisible(bool visible)
    {
        if (Button.gameObject.activeSelf != visible)
            Button.gameObject.SetActive(visible);
    }

    private void OnPressed()
    {
        if (_playerController == null)
            _playerController = FindFirstObjectByType<PlayerController>();

        if (_playerController != null)
            _playerController.RequestInteract();
    }
}