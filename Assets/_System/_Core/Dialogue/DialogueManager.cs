using System;
using System.Collections.Generic;
using UnityEngine;

namespace _System._Core.Dialogue
{
    public class DialogueManager: MonoBehaviour
    {
        private static DialogueManager _instance;

        public static DialogueManager Instance
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

                if (_instance == null) _instance = FindAnyObjectByType<DialogueManager>(FindObjectsInactive.Include);

                return _instance;
            }
            set { _instance = value; }
        }

        public enum EDialogueAnimation
        {
            LINEAR,
            WORD    // word by word
        }
        
        [Serializable]
        public struct DialogueData
        {
            public string Title;
            public string Content;
        }
        
        [SerializeField]
        private Queue<DialogueData> _dialogues = new ();

        public void AddDialogue(DialogueData dialogue)
        {
            _dialogues.Enqueue(dialogue);
            DialogueNotify();
        }

        public void DialogueNotify() // when list update or action execute
        {
            
        }
    }
}