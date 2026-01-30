using System;
using System.Runtime.CompilerServices;
using UnityEngine;

public class PlanetComponent : MonoBehaviour
{
    public EPlanetID PlanetID;

    [Header("Animation")]
    public bool AllowRotate = false;
    public float IdleSpeed = 10f;
    public float SelectedSpeed = 50f;
    public float HoverScaleMult = 1.3f;
    public float SelectedScaleMult = 1.8f;
    public float ScaleSpeed = 5f;

    private PlanetSelectionUIController _controller;
    private bool _isSelected = false;
    private bool _isHovered = false;

    private Vector3 _baseScale;
    private Vector3 _targetScale;
    private float _currentSpeed;

    private void Awake()
    {
        _controller = FindFirstObjectByType<PlanetSelectionUIController>();
        _baseScale = transform.localScale;
        _targetScale = Vector3.one;
        _currentSpeed = IdleSpeed;
    }

    private void OnEnable()
    {
        if (_controller != null)
            _controller.OnPlanetSelected += HandleSelectionChanged;
    }

    private void OnDisable()
    {
        if (_controller != null)
            _controller.OnPlanetSelected -= HandleSelectionChanged;
    }

    private void HandleSelectionChanged(EPlanetID planetID)
    {
        _isSelected = planetID == PlanetID;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (_isSelected)
        {
            _targetScale = _baseScale * SelectedScaleMult;
            _currentSpeed = SelectedSpeed;
        }
        else if (_isHovered)
        {
            _targetScale = _baseScale * HoverScaleMult;
            _currentSpeed = IdleSpeed;
        }
        else
        {
            _targetScale = _baseScale;
            _currentSpeed = IdleSpeed;
        }
    }

    private void Update()
    {
        if (AllowRotate)
            transform.Rotate(transform.up * Time.deltaTime * _currentSpeed);

        transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, Time.deltaTime * ScaleSpeed);
    }

    private void OnMouseEnter()
    {
        if (GameManager.Instance.GetGameState() != EGameState.PlanetSelection)
            return;

        _isHovered = true;
        UpdateVisual();
    }

    private void OnMouseExit()
    {
        _isHovered = false;
        UpdateVisual();
    }

    private void OnMouseDown()
    {
        if (GameManager.Instance.GetGameState() != EGameState.PlanetSelection)
            return;

        _controller.SelectPlanet(PlanetID);
    }

}
