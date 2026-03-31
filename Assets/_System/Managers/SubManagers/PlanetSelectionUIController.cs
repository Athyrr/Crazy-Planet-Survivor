using UnityEngine;
using UnityEngine.UI;

public class PlanetSelectionUIController : MonoBehaviour
{
    public delegate void OnPlanetSelectedDelegate(EPlanetID planetID, Transform planetTransform, Vector3 focusOffset);

    public event OnPlanetSelectedDelegate OnPlanetSelected = null;

    public Button ExploreButton;

    private EPlanetID _currentSelectedPlanet;

    private void Awake()
    {
        _currentSelectedPlanet = EPlanetID.None;
    }

    private void OnEnable()
    {
        ExploreButton.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        ExploreButton.gameObject.SetActive(false);
        _currentSelectedPlanet = EPlanetID.None;
    }


    public EPlanetID SelectedPlanet => _currentSelectedPlanet;

    public void SelectPlanet(EPlanetID planetID, Transform planetTransform = null, Vector3 focusOffset = default)
    {
        if (_currentSelectedPlanet == planetID)
        {
            _currentSelectedPlanet = EPlanetID.None;
            planetTransform = null;
        }
        else
        {
            _currentSelectedPlanet = planetID;
        }

        bool hasSelection = _currentSelectedPlanet != EPlanetID.None;
        ExploreButton.gameObject.SetActive(hasSelection);

        if (hasSelection)
            Debug.Log($"[Planet Selection] Selected Planet: {_currentSelectedPlanet}");

        OnPlanetSelected?.Invoke(_currentSelectedPlanet, planetTransform, focusOffset);
    }

    public void ExplorePlanet()
    {
        if (_currentSelectedPlanet == EPlanetID.None)
            return;

        ExploreButton.gameObject.SetActive(false);

        GameManager.Instance.StartRun(_currentSelectedPlanet);
    }

    public void OpenView()
    {
        SelectPlanet(EPlanetID.None);
        ExploreButton.gameObject.SetActive(false);
    }

    public void CloseView()
    {
        ExploreButton.gameObject.SetActive(false);
        _currentSelectedPlanet = EPlanetID.None;
    }
}