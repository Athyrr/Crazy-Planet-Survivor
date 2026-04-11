using UnityEngine;

public class HUDController : UIControllerBase
{
    public void ShowHUD() => gameObject.SetActive(true);
    public void HideHUD() => gameObject.SetActive(false);
}
