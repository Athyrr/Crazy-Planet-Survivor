using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using static UnityEngine.InputSystem.InputAction;

public class PlayerController : MonoBehaviour
{
    private GameInputs _gameInputs;

    private Vector2 _inputDirection = Vector2.zero;

    private void Awake()
    {
        if (_gameInputs == null)
            _gameInputs = new GameInputs();
    }

    void OnEnable()
    {
        _gameInputs.Player.Move.performed += HandleMoveInput;
        _gameInputs.Player.Move.canceled += HandleMoveInput;

        _gameInputs.Enable();
    }

    void OnDisable()
    {
        _gameInputs.Player.Move.performed -= HandleMoveInput;
        _gameInputs.Player.Move.canceled -= HandleMoveInput;

        _gameInputs.Disable();
    }
    void Update()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var entityManager = world.EntityManager;

        InjectInputDirectionToECS(ref entityManager, _inputDirection);
    }

    private void HandleMoveInput(CallbackContext ctx)
    {
        if (ctx.canceled)
            _inputDirection = Vector2.zero;

        _inputDirection = ctx.ReadValue<Vector2>();
    }

    private void InjectInputDirectionToECS(ref EntityManager entityManager, Vector2 vec)
    {
        var inputQuery = entityManager.CreateEntityQuery(typeof(InputData));

        if (inputQuery.IsEmpty)
            return;

        var inputEntity = inputQuery.GetSingletonEntity();
        entityManager.SetComponentData(inputEntity, new InputData { Value = _inputDirection });
    }
}
