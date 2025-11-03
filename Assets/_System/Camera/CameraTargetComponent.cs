using UnityEngine;

/// <summary>
/// Represents the target for the camera to follow.
/// It is implemented as a singleton to ensure only one instance exists in the scene and is used by CinemachineCamera.
/// </summary>
public class CameraTargetComponent : MonoBehaviour
{
    public static CameraTargetComponent Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }
}
