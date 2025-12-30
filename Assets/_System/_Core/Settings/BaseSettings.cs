using System.Linq;
using System.Runtime.CompilerServices;
using _System._Core.Settings;
using UnityEditor;
using UnityEngine;

[assembly: InternalsVisibleTo("EnhancedFramework.Core")]
namespace EnhancedFramework.Core.Settings
{
    /// <summary>
    /// Base class to inherit all game settings from.
    /// </summary>
    /// <typeparam name="T">This class type.</typeparam>
    public abstract class BaseSettings<T> : ScriptableSettings where T : ScriptableSettings
    {
        #region Content
        private static T instance = null;

        /// <summary>
        /// Global shared instance across the entire game.
        /// </summary>
        public static T I
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                #if UNITY_EDITOR
                var _guid = AssetDatabase.FindAssets($"t:{typeof(T).Name}").First();
                if (!(Application.isPlaying && (instance == null) && _guid.Length > 0))
                {
                    instance = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(_guid), typeof(T)) as T;
                }
                #endif

                return instance;
            }
            protected set
            {
                instance = value;
            }
        }

        // -----------------------

        public override void Init()
        {
            I = this as T;
        }

        #endregion
    }

    /// <summary>
    /// Base class to inherit all game database from.
    /// </summary>
    /// <typeparam name="T">This class type.</typeparam>
    public abstract class BaseDatabase<T> : ScriptableSettings where T : ScriptableSettings
    {
        #region Content

        private static T database = null;

        /// <summary>
        /// Global shared instance across the entire game.
        /// </summary>
        public static T Database
        {
            get
            {
                #if UNITY_EDITOR
                var _guid = AssetDatabase.FindAssets($"t:{typeof(T).Name}").First();
                if (!(Application.isPlaying && (database == null) && _guid.Length > 0))
                {
                    database = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(_guid), typeof(T)) as T;
                }
                #endif

                return database;
            }
            protected set
            {
                database = value;
            }
        }

        // -----------------------

        public override void Init()
        {
            Database = this as T;
        }

        #endregion
    }
}
