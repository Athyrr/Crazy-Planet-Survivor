using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Windows;

namespace _System.Settings
{
    public class CPBaseSettings: ScriptableObject
    {
        private static Dictionary<Type, CPBaseSettings> _instances = new();
    
        internal static CPBaseSettings GetInstance(Type type)
        {
            if (!type.IsSubclassOf(typeof(CPBaseSettings))) return null;

            if (!_instances.TryGetValue(type, out var instance) 
                || instance == null)
            {
                var list = Resources.LoadAll("Settings", type);

                if (list != null && list.Length > 0)
                {
                    instance = list[0] as CPBaseSettings;
                    _instances[type] = instance;
                }
#if UNITY_EDITOR
                else if (!type.IsAbstract)
                {
                    instance = CreateAssetOfType(type, "Assets/Resources/Settings/" + type.Name + ".asset") as CPBaseSettings;
                    AssetDatabase.SaveAssets();
                }
#endif
            }

            return instance;
        }
        
#if UNITY_EDITOR
        private static HashSet<EditorDelayedCall> _delayedCalls;
        
        public static UnityEngine.Object CreateAssetOfType(Type type, string path, bool triggerRename = false)
        {
            if (type.IsSubclassOf(typeof(ScriptableObject)))
                return CreateScriptableAsset(type, path, triggerRename);
            
            return null;
        }
        
        public static ScriptableObject CreateScriptableAsset(Type type, string path, bool triggerRename = false)
        {
            if (!path.EndsWith(".asset")) path += ".asset";
            EnsureAssetParentDirectoryExistence(path);
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            var obj = ScriptableObject.CreateInstance(type);
            if (obj != null)
            {
                AssetDatabase.CreateAsset(obj, path);
                AssetDatabase.SaveAssetIfDirty(obj);
                if (triggerRename) TriggerAssetRename(obj);
            }
            return obj;
        }
        
        public static void EnsureAssetParentDirectoryExistence(string assetPath)
        {
            var index = assetPath.LastIndexOf('/');
            if (index != -1)
            {
                string directoryPath = assetPath.Substring(0, index);
                if (!Directory.Exists(directoryPath))
                {
                    string[] pathMembers = directoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    string currentPath = "";

                    for (int i = 0; i < pathMembers.Length; i++)
                    {
                        currentPath += pathMembers[i];
                        if (!Directory.Exists(currentPath))
                        {
                            Directory.CreateDirectory(currentPath);
                        }
                        currentPath += "/";
                    }
                }
            }
        }
        public static void TriggerAssetRename(UnityEngine.Object obj, bool delay = false)
        {
            SelectAndFocusAsset(obj);
            if (delay)
            {
                EditorGUIUtility.PingObject(obj);
                DelayCall(0.2d, SendRenameEvent);
            }
            else
            {
                SendRenameEvent();
            }
        }
        public static void SelectAndFocusAsset(UnityEngine.Object obj)
        {
            EditorUtility.FocusProjectWindow();
            SelectObject(obj);
        }
        
        public static void SelectObject(UnityEngine.Object obj)
        {
            if (obj is GameObject go)
            {
                Selection.activeGameObject = go;
            }
            else
            {
                Selection.activeObject = obj;
            }
        }
        
        private static void SendRenameEvent()
        {
            EditorWindow.focusedWindow.SendEvent(new Event() { keyCode = KeyCode.F2, type = EventType.KeyDown });
        }
        
        public static void DelayCall(double delay, Action callback)
        {
            if (_delayedCalls == null) _delayedCalls = new();

            _delayedCalls.Add(new EditorDelayedCall(EditorApplication.timeSinceStartup + delay, callback));
        }
        
        private struct EditorDelayedCall
        {
            public EditorDelayedCall(double triggerTime, Action callback)
            {
                this.triggerTime = triggerTime;
                this.callback = callback;
            }

            public readonly double triggerTime;
            private Action callback;

            public bool TriggerIfValid(double editorTime)
            {
                if (editorTime >= triggerTime)
                {
                    callback?.Invoke();
                    return true;
                }
                return false;
            }
        }
#endif
    }
    
    public abstract class CPCustomSettings<T> : CPBaseSettings where T : CPCustomSettings<T>
    {
        #region Instance
        
        private static T _instance;
        public static T I
        {
            get
            {
                if (_instance == null)
                {
                    if (GetInstance(typeof(T)) is T t)
                    {
                        _instance = t;
                    }
                }
            
                return _instance;
            }
        }
        
        #endregion
    }
}