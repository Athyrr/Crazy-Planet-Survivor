using UnityEngine;

namespace _System.Settings
{
    [CreateAssetMenu(fileName = "Enemy", menuName = "CPSettings/Enemy")]
    public class CpBaseEnemySettings : CpSettings<CpBaseEnemySettings>
    {
        [Header("Movement Setting")]
        [Tooltip("How fast enemies turn to face the player. It is the lerp factor used as " +
                 "(DeltaTime * value), so it stays framerate-independent. Higher = snappier/less smooth, " +
                 "lower = smoother/slower. 0 freezes the rotation, so keep it above 0.")]
        [SerializeField, Range(0f, 30f)] private float _rotationLerpSpeed = 10f;

        #region Accessor

        public static float RotationLerpSpeed => I._rotationLerpSpeed;

        #endregion
    }
}
