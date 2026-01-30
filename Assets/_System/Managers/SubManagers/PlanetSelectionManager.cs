using UnityEngine;

public class PlanetSelectionManager : MonoBehaviour
{
    public LayerMask GalaxyLayer;

    public GameObject PlanetSelectionPanel;

    private EPlanetID _focusedPlanet;

    public EPlanetID FocusedPlanet => _focusedPlanet;

    private void Awake()
    {
        _focusedPlanet = EPlanetID.None;
    }

    public void SelectPlanet(EPlanetID planetID)
    {
        _focusedPlanet = planetID;
    }

    public void UnSelectPlanet()
    {
        _focusedPlanet = EPlanetID.None;
    }

    private void Update()
    {
        //if (Input.GetKeyDown(KeyCode.Mouse0))
        //{
        //    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        //    if (Physics.Raycast(ray, out RaycastHit hitInfo, 1000f, GalaxyLayer))
        //    {
        //        if (hitInfo.collider.TryGetComponent<PlanetComponent>(out var planet))
        //        {
        //            Debug.Log($"[Lobby] Selected Planet: {planet.PlanetID}");

        //            GameManager.Instance.LoadPlanetSubScene(planet.PlanetID);
        //            EGameState newState = planet.PlanetID == EPlanetID.Lobby ? EGameState.Lobby : EGameState.Running;
        //            GameManager.Instance.ChangeState(newState);
        //        }
        //    }
        //}


        if (_focusedPlanet != EPlanetID.None && !PlanetSelectionPanel.activeSelf)
            PlanetSelectionPanel.SetActive(true);
        else if (_focusedPlanet == EPlanetID.None && PlanetSelectionPanel.activeSelf)
            PlanetSelectionPanel.SetActive(false);


        Debug.Log("Selected Planet: " + _focusedPlanet);
    }

    public void LaunchPlanet()
    {
        GameManager.Instance.LoadPlanetSubScene(_focusedPlanet);
        EGameState newState = _focusedPlanet == EPlanetID.Lobby ? EGameState.Lobby : EGameState.Running;
        GameManager.Instance.ChangeState(newState);
    }
}
