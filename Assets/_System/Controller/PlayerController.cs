using _System.Settings;
using static UnityEngine.InputSystem.InputAction;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    private RunManager _runManager;
    private GameInputs _gameInputs;
    private EntityQuery _inputQuery;
    private Vector2 _inputDirection = Vector2.zero;
    
    private Vector2 _virtualDirection = Vector2.zero;
    private bool _hasVirtualInput = false;

    private bool _isInteractPressed = false;

    private Vector2 _previewLerpValue =  Vector2.zero;

    private void Awake()
    {
        _gameInputs = new GameInputs();

        if (_runManager == null)
            _runManager = FindAnyObjectByType<RunManager>();
    }

    private void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        _inputQuery = world.EntityManager.CreateEntityQuery(typeof(InputData));
    }

    private void OnEnable()
    {
        _gameInputs.Player.Move.performed += HandleMoveInput;
        _gameInputs.Player.Move.canceled += HandleMoveInput;
        _gameInputs.Player.Pause.started += HandlePauseInput;
        _gameInputs.Player.Interact.performed += HandleInteractInput;

        _gameInputs.Enable();
    }

    private void OnDisable()
    {
        _gameInputs.Player.Move.performed -= HandleMoveInput;
        _gameInputs.Player.Move.canceled -= HandleMoveInput;
        _gameInputs.Player.Pause.started -= HandlePauseInput;
        _gameInputs.Player.Interact.performed -= HandleInteractInput;

        _gameInputs.Disable();
    }

    private void Update()
    {
        Vector2 direction = _hasVirtualInput ? _virtualDirection : _inputDirection;
        Vector2 lerpValue = new Vector2(
            math.lerp(_previewLerpValue.x, direction.x, Time.deltaTime * CpBasePlayerSettings.PlayerMovementMitigationSpeed), 
            math.lerp(_previewLerpValue.y, direction.y, Time.deltaTime * CpBasePlayerSettings.PlayerMovementMitigationSpeed));
        
        InjectInputDirectionToECS(lerpValue);
        // Shader.SetGlobalFloat("_BATBlend", lerpValue.magnitude);
        Shader.SetGlobalFloat("_BlendingValue", lerpValue.magnitude);
        
        _previewLerpValue = lerpValue;
    }

    private void HandleMoveInput(CallbackContext ctx)
    {
        if (ctx.control?.device is Touchscreen)
            return;

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
        _isInteractPressed = true;
    }

    public void RequestInteract() => _isInteractPressed = true;
    
    public void SetVirtualMoveInput(Vector2 direction)
    {
        _virtualDirection = direction;
        _hasVirtualInput = true;
    }

    public void ClearVirtualMoveInput()
    {
        _virtualDirection = Vector2.zero;
        _hasVirtualInput = false;
    }
}