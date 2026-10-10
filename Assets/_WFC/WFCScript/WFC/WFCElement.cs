using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace WFCContent.WFC
{
    public class WFCElement : MonoBehaviour
    {
        [SerializeField] private WFCElementData data;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // seulement sur une instance du prefab (scene), pas dans l'asset lui-meme (sinon Prefab = null)
            if (data == null)
                return;
            var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(gameObject);
            if (instanceRoot == null)
                return;

            GameObject prefabRoot = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
            data.Prefab = prefabRoot;
        }
#endif
    }
}