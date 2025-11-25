using UnityEngine;

public class Billboard : MonoBehaviour
{
    public enum BillboardType
    {
        LookAtCamera,
        CameraForward,
        VerticalOnly,  // Only rotate around Y axis
        HorizontalOnly // Only rotate around X axis
    }

    [SerializeField] private BillboardType billboardType = BillboardType.CameraForward;
    [SerializeField] private bool reverseFace = false;
    
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("No camera found with 'MainCamera' tag");
        }
    }

    void LateUpdate()
    {
        if (mainCamera == null) return;

        switch (billboardType)
        {
            case BillboardType.LookAtCamera:
                Vector3 lookDir = mainCamera.transform.position - transform.position;
                if (reverseFace) lookDir = -lookDir;
                transform.rotation = Quaternion.LookRotation(lookDir);
                break;

            case BillboardType.CameraForward:
                Vector3 forward = reverseFace ? -mainCamera.transform.forward : mainCamera.transform.forward;
                transform.rotation = Quaternion.LookRotation(forward);
                break;

            case BillboardType.VerticalOnly:
                Vector3 direction = mainCamera.transform.position - transform.position;
                direction.y = 0; // Ignore Y axis
                if (reverseFace) direction = -direction;
                transform.rotation = Quaternion.LookRotation(direction);
                break;

            case BillboardType.HorizontalOnly:
                Vector3 horizontalDir = mainCamera.transform.position - transform.position;
                horizontalDir.x = 0; // Ignore X axis
                if (reverseFace) horizontalDir = -horizontalDir;
                transform.rotation = Quaternion.LookRotation(horizontalDir);
                break;
        }
    }

    // Editor helper to preview billboard effect in scene view
    #if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
        {
            Camera sceneCamera = UnityEditor.SceneView.currentDrawingSceneView?.camera;
            if (sceneCamera != null)
            {
                Vector3 forward = reverseFace ? -sceneCamera.transform.forward : sceneCamera.transform.forward;
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(transform.position, forward * 2f);
            }
        }
    }
    #endif
}