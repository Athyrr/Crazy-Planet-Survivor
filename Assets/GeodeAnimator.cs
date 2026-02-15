using System;
using System.Collections.Generic;
using EasyButtons;
using PrimeTween;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

public class GeodeAnimator : MonoBehaviour
{
    [SerializeField] private GameObject _hammer;
    [SerializeField] private GameObject _geode;
    [SerializeField] private List<MeshRenderer> _geodeMr;
    [SerializeField] private GameObject _geodeP0;
    [SerializeField] private GameObject _geodeP1;

    public event Action<Vector3> OnGeodeClicked;
    private InputAction _clickAction;

    private bool _init = false;
    private Vector3 _baseGemsPos = new();
    private float _baseDistanceFromCamera;
    private Bounds _bounds;
    private float DistanceFromCamera(Vector3 pos) => Vector3.Distance(_baseGemsPos, pos);

    public Vector3 GetWorldSpaceCursorLocation()
    {
        var cursorLocation = Mouse.current.position.ReadValue();
        var cursorLocationWs = Camera.main.ScreenToWorldPoint(
            new Vector3(cursorLocation.x, cursorLocation.y, _baseDistanceFromCamera)
        );

        return cursorLocationWs;
    }

    private void OnEnable()
    {
        _baseGemsPos = _geode.transform.position;
        _baseDistanceFromCamera = Vector3.Distance(_geode.transform.position, GetWorldSpaceCursorLocation());

        for (int i = 0; i < _geodeMr.Count; i++)
        {
            var el = _geodeMr[i];
            
            _bounds.min = Vector3.Min(_bounds.min, el.bounds.min);
            _bounds.max = Vector3.Max(_bounds.max, el.bounds.max);
        }
        
        _clickAction = new InputAction(type: InputActionType.Button, binding: "<Mouse>/leftButton");
        _clickAction.performed += OnClick;
        _clickAction.Enable();
        
        _init = true;
    }

    private void OnDisable()
    {
        _clickAction.Disable();
    }

    private void OnClick(InputAction.CallbackContext ctx)
    {
        Jitter();
        PlayHammerHit();
    }
    
    private void Jitter()
    {
        var cursorWs = GetWorldSpaceCursorLocation();
        OnGeodeClicked?.Invoke(cursorWs);
        
        var offset = (cursorWs - _bounds.center * 0.01f) * -1;
        Debug.Log($"click, {offset}");

        Tween.ShakeLocalPosition(_geode.transform, strength: new Vector3(0, 1), duration: 1, frequency: 10);
        Tween.ShakeLocalRotation(_geode.transform, strength: new Vector3(0, 0, 15), duration: 1, frequency: 10);
    }
    
    private void PlayHammerHit()
    {
        var hammerTr = _hammer.transform;

        Vector3 startPos = hammerTr.localPosition;
        Quaternion startRot = hammerTr.localRotation;

        Vector3 hitDir = (_geode.transform.position - _hammer.transform.position).normalized;

        Vector3 raiseOffset = -hitDir * 0.5f;
        Vector3 hitOffset   = hitDir * 0.8f;

        var seq = Sequence.Create();

        seq.Group(Tween.LocalPosition(hammerTr, startPos + raiseOffset, 0.08f, Ease.OutQuad));
        seq.Group(Tween.LocalRotation(hammerTr, Quaternion.Euler(30f, 0, 0), 0.08f, Ease.OutQuad));

        seq.Chain(Tween.LocalPosition(hammerTr, startPos + hitOffset, 0.06f, Ease.InQuad));
        seq.Group(Tween.LocalRotation(hammerTr, Quaternion.Euler(-60f, 0, 0), 0.06f, Ease.InQuad));

        seq.Group(Tween.ShakeLocalRotation(hammerTr, new Vector3(0, 0, 10), 0.1f, frequency: 25));

        seq.Chain(Tween.LocalPosition(hammerTr, startPos, 0.12f, Ease.OutBack));
        seq.Group(Tween.LocalRotation(hammerTr, startRot, 0.12f, Ease.OutBack));
    }

    [Button]
    private void PlayAnimation()
    {
        Debug.Log("Play animation");
        var punchDir = Random.insideUnitSphere;
        Tween.PunchLocalPosition(_geode.transform, strength: punchDir, duration: 0.2f, frequency: 10);
    }

    private void Update()
    {
        if (!_init)
            return;

        var cursorWs = GetWorldSpaceCursorLocation();
        var dist = DistanceFromCamera(cursorWs);

        Vector3 pos;
        if (dist < 2f)
            pos = Vector3.Lerp(_geode.transform.position, cursorWs, Time.deltaTime / (dist * 0.5f));
        else
            pos = Vector3.Lerp(_geode.transform.position, _baseGemsPos, Time.deltaTime);

        _geode.transform.position = pos;

        _hammer.transform.position = cursorWs;
        
        _geode.transform.rotation = Quaternion.Euler(_geode.transform.rotation.eulerAngles +
                                                     (new Vector3(1f, 1f, 0) * Time.deltaTime * 10f));
    }
}