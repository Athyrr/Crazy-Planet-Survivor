using UnityEngine;

public class PlanetSelectionUIController : MonoBehaviour
{
    public delegate void OnPlanetSelectedDelegate(EPlanetID planetID);
    public event OnPlanetSelectedDelegate OnPlanetSelected = null;

    [Header("UI References")]

    public GameObject PlanetSelectionPanel;

    private EPlanetID _currentSelectedPlanet;

    private void Awake()
    {
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
        PlanetSelectionPanel.SetActive(hasSelection);

        if (hasSelection)
            Debug.Log($"[Planet Selection] Selected Planet: {_currentSelectedPlanet}");

        OnPlanetSelected?.Invoke(_currentSelectedPlanet);
    }


    public void ExplorePlanet()
    {
        if (_currentSelectedPlanet == EPlanetID.None)
            return;

        GameManager.Instance.LoadPlanetSubScene(_currentSelectedPlanet);
    }

    public void OpenView()
    {
        SelectPlanet(EPlanetID.None);
    }

    public void CloseView()
    {
        PlanetSelectionPanel.SetActive(false);
        _currentSelectedPlanet = EPlanetID.None;
    }
}
