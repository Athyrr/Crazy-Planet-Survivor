using System.Runtime.CompilerServices;
using UnityEngine;

public class PlanetComponent : MonoBehaviour
{

    private PlanetSelectionManager _planetSelectionManager;

    public bool AllowRotate = false;
    public float IdleSpeed = 10f;
    public float FocusSpeed = 20f;

    public EPlanetID PlanetID;

    private bool _isFocused = false;

    private Vector3 _baseScale;

    private void Awake()
    {
        _planetSelectionManager = FindFirstObjectByType<PlanetSelectionManager>();

        _baseScale = transform.localScale;
    }

    private void Update()
    {
        Rotate(); ;
    }

    private void Rotate()
    {
        float speed = _isFocused ? FocusSpeed : IdleSpeed;

        if (AllowRotate)
        {
            transform.Rotate(transform.up * Time.deltaTime * speed, Space.World);
        }
    }

    private void OnMouseEnter()
    {
        transform.localScale = _baseScale * 1.3f;
        _isFocused = true;
    }

    private void OnMouseDown()
    {
        if (!_isFocused)
            return;

        if (_planetSelectionManager.FocusedPlanet == PlanetID)
            _planetSelectionManager.UnSelectPlanet();
        else
            _planetSelectionManager.SelectPlanet(PlanetID);
    }

    private void OnMouseExit()
    {
        transform.localScale = _baseScale;
        _isFocused = false;
    }

}
