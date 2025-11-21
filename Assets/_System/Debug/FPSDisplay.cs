using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple FPS counter for debugging (works in Editor and on mobile)
/// </summary>
public class FPSDisplay : MonoBehaviour
{
    public bool enable;
    public Text fpsText;              // Optional: link a UI Text in the Canvas
    public Color textColor = Color.white;
    public int fontSize = 30;
    

    private float deltaTime = 0.0f;
    private GUIStyle guiStyle = new GUIStyle();

    void Update()
    {
        if (!enable) return;

        // Calculate FPS
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
    }
    
#if UNITY_EDITOR
    void OnValidate()
    {
        if (fpsText == null)
        {
            // Try to find existing Text in children
            fpsText = GetComponentInChildren<Text>();
            if (fpsText == null)
            {
                // Ensure there's a Canvas
                Canvas canvas = GetComponentInParent<Canvas>();
                if (canvas == null)
                {
                    GameObject canvasObj = new GameObject("FPSCanvas", typeof(Canvas));
                    canvas = canvasObj.GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                }

                // Create the Text UI element
                GameObject textObj = new GameObject("FPSText", typeof(Text));
                textObj.transform.SetParent(canvas.transform, false);

                fpsText = textObj.GetComponent<Text>();
                fpsText.text = "FPS: 0";
                fpsText.color = textColor;
                fpsText.fontSize = fontSize;
                fpsText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

                // Position at top-left
                RectTransform rect = fpsText.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0, 1);
                rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(10, -10);

                Debug.Log("[FPSDisplay] Created new FPS Text UI element.");
            }
        }
    }
#endif
    
    void OnGUI()
    {
        if (!enable) return;
        
        int w = Screen.width, h = Screen.height;
        guiStyle.alignment = TextAnchor.UpperLeft;
        guiStyle.fontSize = h * 2 / 50;
        guiStyle.normal.textColor = textColor;

        float msec = deltaTime * 1000.0f;
        float fps = 1.0f / deltaTime;
        string text = string.Format("{0:0.0} ms ({1:0.} FPS)", msec, fps);

        Rect rect = new Rect(10, 10, w, h * 2 * 0.01f);
        GUI.Label(rect, text, guiStyle);

        // Optional: update linked Text component
        if (fpsText != null)
            fpsText.text = text;
    }
}