using UnityEditor;
using UnityEngine;
using WFCContent.World;

namespace Editor
{
    [CustomEditor(typeof(FreeFormDeformerElement))]
    public class FFDElementEditor: UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            var el = target as FreeFormDeformerElement;

            var anchors = el.GetRelativeAnchors();
            for (var index = 0; index < anchors.Count; index++)
            {
                var anchor = anchors[index];
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.PositionHandle(anchor, Quaternion.identity);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(el, "Move Point");
                    el.Anchors[index] = el.transform.InverseTransformPoint(newPos);
                    EditorUtility.SetDirty(el);
                }
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var el = target as FreeFormDeformerElement;

            if (GUILayout.Button("Compute Triangulated Anchors"))
            {
                Undo.RecordObject(el, "Compute Triangulated Anchors");
                el.ComputeTriangulatedAnchors();
                EditorUtility.SetDirty(el);
            }
        }
    }
}
