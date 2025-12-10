using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using static UnityEngine.InputSystem.InputAction;

public class PlayerController : MonoBehaviour
{
    public GameManager GameManager;

    private GameInputs _gameInputs;

    private EntityQuery _inputQuery;

    private Vector2 _inputDirection = Vector2.zero;
    private bool _isInteractPressed = false;

    private void Awake()
    {
        if (_gameInputs == null)
            _gameInputs = new GameInputs();
    }

    private void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;

        var entityManager = world.EntityManager;
        _inputQuery = entityManager.CreateEntityQuery(typeof(InputData));
    }

    void OnEnable()
    {
        _gameInputs.Player.Move.performed += HandleMoveInput;
        _gameInputs.Player.Move.canceled += HandleMoveInput;

        _gameInputs.Player.Pause.started += HandlePauseInput;

        _gameInputs.Player.Interact.started += HandleInteractInput;

        _gameInputs.Enable();
    }

    void OnDisable()
    {
        _gameInputs.Player.Move.performed -= HandleMoveInput;
        _gameInputs.Player.Move.canceled -= HandleMoveInput;

        _gameInputs.Player.Pause.started -= HandlePauseInput;

        _gameInputs.Player.Interact.started -= HandleInteractInput;

        _gameInputs.Disable();
    }
    void Update()
    {
        InjectInputDirectionToECS(_inputDirection);
    }

    private void HandleMoveInput(CallbackContext ctx)
    {
        if (ctx.canceled)
            _inputDirection = Vector2.zero;

        _inputDirection = ctx.ReadValue<Vector2>();
    }

    private void InjectInputDirectionToECS(Vector2 direction)
    {
        if (_inputQuery.IsEmpty)
            return;

        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        var inputEntity = _inputQuery.GetSingletonEntity();

        entityManager.SetComponentData(inputEntity,
            new InputData
            {
                Value = direction,
                IsInteractPressed = _isInteractPressed
            });

        _isInteractPressed = false;
    }

    private void HandlePauseInput(CallbackContext ctx)
    {
        if (ctx.started)
            GameManager.TogglePause();
    }

    private void HandleInteractInput(CallbackContext ctx)
    {
        if (ctx.started)
            _isInteractPressed = true;
    }
}
