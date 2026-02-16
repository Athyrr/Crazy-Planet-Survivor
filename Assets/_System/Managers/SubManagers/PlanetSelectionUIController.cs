using UnityEngine;
using UnityEngine.UI;

public class PlanetSelectionUIController : MonoBehaviour
{
    public delegate void OnPlanetSelectedDelegate(EPlanetID planetID);
    public event OnPlanetSelectedDelegate OnPlanetSelected = null;

    public Button ExploreButton;

    private EPlanetID _currentSelectedPlanet;

    private void Awake()
    {
        _currentSelectedPlanet = EPlanetID.None;
    }

    private void OnEnable()
    {
    }

    private void OnDisable()
    {
        ExploreButton.gameObject.SetActive(false);
        _currentSelectedPlanet = EPlanetID.None;
    }


    public EPlanetID SelectedPlanet => _currentSelectedPlanet;

    public void SelectPlanet(EPlanetID planetID)
    {
        if (_currentSelectedPlanet == planetID)
            _currentSelectedPlanet = EPlanetID.None;
        else
            _currentSelectedPlanet = planetID;

        // Is a planet selected?
        bool hasSelection = _currentSelectedPlanet != EPlanetID.None;
        ExploreButton.gameObject.SetActive(hasSelection);

        if (hasSelection)
            Debug.Log($"[Planet Selection] Selected Planet: {_currentSelectedPlanet}");

        OnPlanetSelected?.Invoke(_currentSelectedPlanet);
    }

    public void ExplorePlanet()
    {
        if (_currentSelectedPlanet == EPlanetID.None)
            return;

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
