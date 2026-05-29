using Unity.Entities;
using Unity.Physics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Ray = UnityEngine.Ray;
using RaycastHit = Unity.Physics.RaycastHit;

/// <summary>
/// Routes a UI.Click tap into the ECS world: raycasts the gameplay camera against the
/// PhysicsWorld and, if the hit entity carries <see cref="Interactable"/>, spawns the
/// matching Open*Request event entity. Bypasses the in-range tag — tapping a building
/// from anywhere opens it.
/// </summary>
public class LobbyTouchInteractDispatcher : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Camera used to project the pointer ray. Falls back to Camera.main when null.")]
    public Camera GameplayCamera;

    [Tooltip("Max ray distance in world units.")]
    public float MaxRayDistance = 1000f;

    private GameInputs _inputs;
    private EntityManager _entityManager;
    private EntityQuery _physicsWorldQuery;
    private bool _initialized;

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
        _physicsWorldQuery = _entityManager.CreateEntityQuery(typeof(PhysicsWorldSingleton));
        _initialized = true;
    }

    private void OnClick(InputAction.CallbackContext ctx)
    {
        // UI.Click is PassThrough Button — performed fires on both press and release.
        if (!ctx.ReadValueAsButton())
            return;

        // Skip taps the EventSystem already consumed (HUD buttons, upgrade cards, etc.)
        // so a tap on the on-screen Interact button doesn't double-fire.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        EnsureInit();
        if (!_initialized || _physicsWorldQuery.IsEmpty)
            return;

        Camera cam = GameplayCamera != null ? GameplayCamera : Camera.main;
        if (cam == null)
            return;

        Vector2 pointerPos = _inputs.UI.Point.ReadValue<Vector2>();
        Ray ray = cam.ScreenPointToRay(pointerPos);

        var raycastInput = new RaycastInput
        {
            Start = ray.origin,
            End = ray.origin + ray.direction * MaxRayDistance,
            Filter = CollisionFilter.Default,
        };

        var physicsWorld = _physicsWorldQuery.GetSingleton<PhysicsWorldSingleton>();
        if (!physicsWorld.CastRay(raycastInput, out RaycastHit hit))
            return;

        Entity hitEntity = hit.Entity;
        if (!_entityManager.HasComponent<Interactable>(hitEntity))
            return;

        var interactable = _entityManager.GetComponentData<Interactable>(hitEntity);
        CreateInteractRequest(interactable.InteractionType);
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
