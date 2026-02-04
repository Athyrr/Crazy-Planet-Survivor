using static UnityEngine.InputSystem.InputAction;
using Unity.Entities;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    private RunManager _runManager;
    private GameInputs _gameInputs;
    private EntityQuery _inputQuery;
    private Vector2 _inputDirection = Vector2.zero;
    private bool _isInteractPressed = false;

    private void Awake()
    {
        _gameInputs = new GameInputs();

        if (_runManager == null) 
            _runManager = FindFirstObjectByType<RunManager>();
    }

    private void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        _inputQuery = world.EntityManager.CreateEntityQuery(typeof(InputData));
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
            _runManager.TogglePause();
    }

    private void HandleInteractInput(CallbackContext ctx)
    {
        if (ctx.started)
            _isInteractPressed = true;
    }

}
