using UnityEngine;

namespace _System._Core.Settings
{
    /// <summary>
    /// Base class to inherit from for all any game-related settings.
    /// </summary>
    public abstract class ScriptableSettings : ScriptableObject
    {
        #region Content

        /// <summary>
        /// Initialize and set this settings instance as the general one to be used for the game.
        /// </summary>
        public abstract void Init();

        #endregion
    }
}