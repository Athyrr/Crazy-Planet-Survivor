using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BATAnimGraph))]
public class BATAnimGraphEditor : UnityEditor.Editor
{
    private SerializedProperty _animations;
    private SerializedProperty _animationSelector;
    private SerializedProperty _globalSpeed;
    private SerializedProperty _autoPlay;
    private SerializedProperty _crossfadeDuration;
    private SerializedProperty _enableCrossfade;
    private SerializedProperty _meshRenderer;
    private SerializedProperty _materialIndex;

    private void OnEnable()
    {
        _animations = serializedObject.FindProperty("animations");
        _animationSelector = serializedObject.FindProperty("animationSelector");
        _globalSpeed = serializedObject.FindProperty("globalSpeed");
        _autoPlay = serializedObject.FindProperty("autoPlay");
        _crossfadeDuration = serializedObject.FindProperty("crossfadeDuration");
        _enableCrossfade = serializedObject.FindProperty("enableCrossfade");
        _meshRenderer = serializedObject.FindProperty("meshRenderer");
        _materialIndex = serializedObject.FindProperty("materialIndex");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var graph = (BATAnimGraph)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("BAT Animation Graph", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(_animations, true);

        if (_animations.arraySize > 1)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Animation Slider", EditorStyles.boldLabel);

            int currentIndex = Mathf.RoundToInt(_animationSelector.floatValue * (_animations.arraySize - 1));

            // Display current animation name
            var animData = _animations.GetArrayElementAtIndex(currentIndex);
            var clipNameProp = animData.FindPropertyRelative("clipName");
            string currentName = clipNameProp != null ? clipNameProp.stringValue : $"Animation {currentIndex}";
            EditorGUILayout.LabelField("Current:", currentName);

            EditorGUILayout.Slider(_animationSelector, 0f, 1f);

            EditorGUILayout.BeginHorizontal();
            int currentDisplay = Mathf.RoundToInt(currentIndex);
            int newIndex = EditorGUILayout.IntSlider("Index", currentDisplay, 0, _animations.arraySize - 1);
            if (newIndex != currentDisplay)
                _animationSelector.floatValue = _animations.arraySize > 1
                    ? (float)newIndex / (_animations.arraySize - 1)
                    : 0f;
            EditorGUILayout.EndHorizontal();

            // Show animation labels
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < _animations.arraySize; i++)
            {
                var elem = _animations.GetArrayElementAtIndex(i);
                var nameProp = elem.FindPropertyRelative("clipName");
                string label = nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue)
                    ? nameProp.stringValue
                    : $"[{i}]";
                if (i == currentIndex)
                    GUI.color = Color.green;
                else
                    GUI.color = Color.gray;

                float width = (EditorGUIUtility.currentViewWidth - 30f) / _animations.arraySize;
                if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(width)))
                {
                    _animationSelector.floatValue = _animations.arraySize > 1
                        ? (float)i / (_animations.arraySize - 1)
                        : 0f;
                }
                GUI.color = Color.white;
            }
            EditorGUILayout.EndHorizontal();
        }
        else if (_animations.arraySize == 1)
        {
            var animData = _animations.GetArrayElementAtIndex(0);
            var clipNameProp = animData.FindPropertyRelative("clipName");
            string name = clipNameProp != null ? clipNameProp.stringValue : "Animation 0";
            EditorGUILayout.LabelField("Animation:", name);
        }
        else
        {
            EditorGUILayout.HelpBox("Add BATAnimationData assets to the list.", MessageType.Info);
        }

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(_globalSpeed);
        EditorGUILayout.PropertyField(_autoPlay);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Crossfade", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_enableCrossfade);
        using (new EditorGUI.DisabledScope(!_enableCrossfade.boolValue))
        {
            EditorGUILayout.PropertyField(_crossfadeDuration);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Renderer", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_meshRenderer);
        EditorGUILayout.PropertyField(_materialIndex);

        serializedObject.ApplyModifiedProperties();

        if (Application.isPlaying && _animations.arraySize > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(graph.autoPlay ? "Pause" : "Play"))
            {
                if (graph.autoPlay) graph.Pause(); else graph.Play();
            }
            if (GUILayout.Button("Reset"))
            {
                graph.ResetTime();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
