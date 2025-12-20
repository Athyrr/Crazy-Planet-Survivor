using System;
using UnityEngine;

namespace _System._Core.Dialogue
{
    public class DialogueUIManager: MonoBehaviour
    {
        private static DialogueUIManager _instance;

        public static DialogueUIManager Instance
        {
            get
            {
#if UNITY_EDITOR
                try
                {
                    if (Application.isPlaying == false) return null;
                }
                catch (UnityException e)
                {
                    Debug.LogError("[CATCH SO NOT CANCELING CODE] " + e.Message);
                }
#endif

                if (_instance == null) _instance = FindAnyObjectByType<DialogueUIManager>(FindObjectsInactive.Include);

                return _instance;
            }
            set { _instance = value; }
        }
        
    }
}