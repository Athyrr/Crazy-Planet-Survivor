using UnityEngine;

namespace WFCContent.HE
{
    public sealed class HESingleton
    {
        private static volatile HESingleton instance;
        private static object syncRoot = new Object();

        private HESingleton() {}

        public static HESingleton Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (syncRoot)
                    {
                        if (instance == null)
                            instance = new HESingleton();
                    }
                }

                return instance;
            }
        }
    }
    // partage par tous les singletons: l'appli quitte, on ne recree pas d'instance fantome
    internal static class HESingletonState
    {
        public static bool Quitting;

        // remis a zero a chaque lancement, meme sans rechargement de domaine (Enter Play Mode Options)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Quitting = false;

#if UNITY_EDITOR
        // en sortant du play mode l'editeur doit retrouver ses singletons (Generate de la planete)
        [UnityEditor.InitializeOnLoadMethod]
        private static void ListenPlayMode() =>
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
                    Quitting = false;
            };
#endif
    }

    public class HESingleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T _instance;
        private static object _lock = new object();
        public static T Instance
        {
            get
            {
                if (HESingletonState.Quitting) {
                    Debug.LogWarning("[Singleton] Instance '"+ typeof(T) +
                        "' already destroyed on application quit." +
                        " Won't create again - returning null.");
                    return null;
                }
                lock(_lock)
                {
                    if (_instance == null)
                    {
                        _instance = (T) FindObjectOfType(typeof(T));
                        if ( FindObjectsOfType(typeof(T)).Length > 1 )
                        {
                            Debug.LogError("[Singleton] Something went really wrong " +
                                " - there should never be more than 1 singleton!" +
                                " Reopening the scene might fix it.");
                            return _instance;
                        }
                        if (_instance == null)
                        {
                            GameObject singleton = new GameObject();
                            _instance = singleton.AddComponent<T>();
                            singleton.name = "(singleton) "+ typeof(T).ToString();
                            DontDestroyOnLoad(singleton);
                            Debug.Log("[Singleton] An instance of " + typeof(T) +
                                " is needed in the scene, so '" + singleton +
                                "' was created with DontDestroyOnLoad.");
                        } else {
                            Debug.Log("[Singleton] Using instance already created: " +
                                _instance.gameObject.name);
                        }
                    }
                    return _instance;
                }
            }
        }
        /// <summary>
        /// When Unity quits, it destroys objects in a random order.
        /// If any script calls Instance after it have been destroyed,
        ///   it will create a buggy ghost object that will stay on the Editor scene
        ///   even after stopping playing the Application. Really bad!
        /// So, this was made to be sure we're not creating that buggy ghost object.
        /// A destroyed singleton (scene unloaded) is not a quit: the next scene's instance must still be found.
        /// </summary>
        public void OnApplicationQuit () {
            HESingletonState.Quitting = true;
        }

        public void OnDestroy () {
            if (_instance == this)
                _instance = null;
        }
    }
    /// <summary>
    /// Loads the single asset of type T stored under Resources/Singletons.
    /// Access Instance from Unity's main thread.
    /// </summary>
    public class HESingletonSO<T> : ScriptableObject where T : ScriptableObject
    {
        private static T _instance;

        public static T Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                var instances = Resources.LoadAll<T>("Singletons");
                if (instances.Length == 0)
                {
                    throw new System.InvalidOperationException(
                        $"[Singleton] No {typeof(T).Name} asset found in Resources/Singletons. " +
                        "Place the configured asset in that folder.");
                }

                if (instances.Length > 1)
                {
                    throw new System.InvalidOperationException(
                        $"[Singleton] Found {instances.Length} {typeof(T).Name} assets in Resources/Singletons. " +
                        "Keep exactly one asset of this type.");
                }

                _instance = instances[0];
                return _instance;
            }
        }
    }

}
