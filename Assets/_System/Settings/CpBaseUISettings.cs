using UnityEngine;

namespace _System.Settings
{
    [CreateAssetMenu(fileName = "UISettings", menuName = "CPSettings/UISettings")]
    public class CpBaseUISettings: CPCustomSettings<CpBaseUISettings>
    {
        [Header("Projet Setting")] 
        [SerializeField] private Color _mainColor = Color.dodgerBlue;
        [SerializeField] private Color _mainColorOver;
        
        [SerializeField] private Color _secondColor;
        [SerializeField] private Color _secondColorOver;
        
        [SerializeField] private Color _complementaryColor;
        [SerializeField] private Color _complementaryColorOver;

        [SerializeField, Range(0, 1)] private float _offElementOpacity;
        
        #region Accessor

        public static Color MainColor => I._mainColor;
        public static Color MainColorOver => I._mainColorOver;

        public static Color SecondColor => I._secondColor;
        public static Color SecondColorOver => I._secondColorOver;

        public static Color ComplementaryColor => I._complementaryColor;
        public static Color ComplementaryColorOver => I._complementaryColorOver;

        public static float OffElementOpacity => I._offElementOpacity;

        #endregion
    }
}