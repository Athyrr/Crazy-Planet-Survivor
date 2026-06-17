using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Allows click/interact on <see cref="Interactable"/> clickable entities.
/// The nearest hit spawns the matching Open*Request event entity, so tapping a building from anywhere opens it.
/// </summary>
public class LobbyTouchInteractDispatcher : MonoBehaviour
{
    [Header("Setup")] [Tooltip("Camera used to project the pointer ray. Falls back to Camera.main when null.")]
    public Camera GameplayCamera;

    [Tooltip("World-space radius of the clickable sphere around each building. " +
             "Leave <= 0 to use each interactable's own interaction Radius.")]
    public float ClickRadiusWorld = 0f;

    private GameInputs _inputs;
    private EntityManager _entityManager;
    private EntityQuery _interactableQuery;
    private bool _initialized;
    private bool _clickQueued;

    private void OnEnable()
    {
        if (_inputs == null)
            _inputs = new GameInputs();

        _inputs.UI.Click.performed += OnClick;
        _inputs.UI.Enable();
    }

    private void OnDisable()
    {
        if (_inputs == null)
            return;

        _inputs.UI.Click.performed -= OnClick;
        _inputs.UI.Disable();
    }

    private void EnsureInit()
    {
        if (_initialized)
            return;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        _entityManager = world.EntityManager;
        _interactableQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Interactable>(),
            ComponentType.ReadOnly<LocalTransform>());
        _initialized = true;
    }

    // UI.Click performed fires on both press and release. We only queue the press here and do the actual
    // work in Update(): IsPointerOverGameObject() must NOT be read from inside an InputAction callback
    // (it runs during input-event processing and would query stale, last-frame UI state — Unity warns).
    private void OnClick(InputAction.CallbackContext ctx)
    {
        if (ctx.ReadValueAsButton())
            _clickQueued = true;
    }

    private void Update()
    {
        if (!_clickQueued)
            return;
        _clickQueued = false;

        // Skip taps the EventSystem already consumed (HUD buttons, upgrade cards, etc.).
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // Buildings are only clickable in the lobby
        if (GameManager.Instance == null || GameManager.Instance.GetGameState() != EGameState.Lobby)
            return;

        EnsureInit();
        if (!_initialized || _interactableQuery.IsEmpty)
            return;

        Camera cam = GameplayCamera != null ? GameplayCamera : Camera.main;
        if (cam == null)
            return;

        Vector2 pointerPos = _inputs.UI.Point.ReadValue<Vector2>();
        Ray ray = cam.ScreenPointToRay(pointerPos);
        float3 origin = ray.origin;
        float3 dir = math.normalize((float3)ray.direction);

        var entities = _interactableQuery.ToEntityArray(Allocator.Temp);
        var interactables = _interactableQuery.ToComponentDataArray<Interactable>(Allocator.Temp);
        var transforms = _interactableQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        int bestIndex = -1;
        float bestT = float.MaxValue;

        for (int i = 0; i < entities.Length; i++)
        {
            float radius = ClickRadiusWorld > 0f ? ClickRadiusWorld : interactables[i].Radius;
            if (radius <= 0f)
                continue;

            if (RayIntersectsSphere(origin, dir, transforms[i].Position, radius, out float t) && t < bestT)
            {
                bestT = t;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0)
            CreateInteractRequest(interactables[bestIndex].InteractionType);

        entities.Dispose();
        interactables.Dispose();
        transforms.Dispose();
    }

    /// <summary>Ray (normalized dir) vs sphere. Returns the nearest non-negative hit distance.</summary>
    private static bool RayIntersectsSphere(float3 origin, float3 dir, float3 center, float radius, out float t)
    {
        t = 0f;
        float3 m = origin - center;
        float b = math.dot(m, dir);
        float c = math.dot(m, m) - radius * radius;

        // Ray starts outside the sphere and points away from it.
        if (c > 0f && b > 0f)
            return false;

        float discriminant = b * b - c;
        if (discriminant < 0f)
            return false;

        t = -b - math.sqrt(discriminant);
        if (t < 0f)
            t = 0f; // origin is inside the sphere

        return true;
    }

    private void CreateInteractRequest(EInteractionType type)
    {
        var requestEntity = _entityManager.CreateEntity();
        switch (type)
        {
            case EInteractionType.PlanetSelection:
                _entityManager.AddComponent<OpenPlanetSelectionViewRequest>(requestEntity);
                break;
            case EInteractionType.MetaProgression:
                _entityManager.AddComponent<OpenMetaProgressionShopRequest>(requestEntity);
                break;
            case EInteractionType.CharacterSelection:
                _entityManager.AddComponent<OpenCharactersShopRequest>(requestEntity);
                break;
            case EInteractionType.AmuletShop:
                _entityManager.AddComponent<OpenAmuletShopRequest>(requestEntity);
                break;
        }
    }
}