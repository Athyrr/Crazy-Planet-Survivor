using PrimeTween;
using UnityEngine;

namespace _System.Settings
{
    [CreateAssetMenu(fileName = "UISettings", menuName = "CPSettings/UISettings")]
    public class CpUISettings: CpSettings<CpUISettings>
    {
        [Header("Project Setting")] 
        [SerializeField] private Color _mainColor = Color.dodgerBlue;
        [SerializeField] private Color _mainColorOver;
        
        [SerializeField] private Color _secondColor;
        [SerializeField] private Color _secondColorOver;
        
        [SerializeField] private Color _complementaryColor;
        [SerializeField] private Color _complementaryColorOver;

        [SerializeField, Range(0, 1)] private float _offElementOpacity;

        [Header("Interactive Labels")]
        [Tooltip("Idle color of an interactive text label (menu button / settings row).")]
        [SerializeField] private Color _labelColor = new Color(0.6528f, 0.6528f, 0.6528f, 1f);
        [Tooltip("Color of a non-interactable (greyed-out) label.")]
        [SerializeField] private Color _labelColorDisabled = new Color(0.42f, 0.42f, 0.48f, 1f);

        [Header("Item Outline")]
        [Tooltip("Outline color of a shop list item when its purchase is committed (selected).")]
        [SerializeField] private Color _itemOutlineSelected = new Color(1f, 0.545f, 0f, 1f);
        [Tooltip("Outline color when the item is hovered/focused and available (unlocked / affordable).")]
        [SerializeField] private Color _itemOutlineHighlight = new Color(0.7547f, 0.6731f, 0.4592f, 1f);
        [Tooltip("Outline color when the item is hovered/focused but locked (not unlocked / can't afford).")]
        [SerializeField] private Color _itemOutlineHighlightLocked = new Color(1f, 0.1373f, 0f, 1f);
        [Tooltip("Outline color when the item is idle and available.")]
        [SerializeField] private Color _itemOutlineIdle = new Color(1f, 0.7412f, 0f, 1f);
        [Tooltip("Outline color when the item is idle and locked.")]
        [SerializeField] private Color _itemOutlineIdleLocked = new Color(0.9623f, 0.3788f, 0.286f, 1f);

        [Header("UI Animation")]
        [Tooltip("Duration (seconds) of a panel slide in/out.")]
        [SerializeField] private float _panelSlideDuration = 0.25f;
        [Tooltip("Ease used when a panel slides in.")]
        [SerializeField] private Ease _panelSlideInEase = Ease.OutCubic;
        [Tooltip("Ease used when a panel slides out.")]
        [SerializeField] private Ease _panelSlideOutEase = Ease.InCubic;
        [Tooltip("Scale multiplier applied to a button/item while hovered or focused.")]
        [SerializeField] private float _hoverScale = 1.05f;
        [Tooltip("Duration (seconds) of the hover scale tween.")]
        [SerializeField] private float _hoverDuration = 0.12f;
        [Tooltip("Ease used for the hover scale tween.")]
        [SerializeField] private Ease _hoverEase = Ease.OutQuad;

        #region Accessor

        public static Color MainColor => I._mainColor;
        public static Color MainColorOver => I._mainColorOver;

        public static Color SecondColor => I._secondColor;
        public static Color SecondColorOver => I._secondColorOver;

        public static Color ComplementaryColor => I._complementaryColor;
        public static Color ComplementaryColorOver => I._complementaryColorOver;

        public static float OffElementOpacity => I._offElementOpacity;

        /// <summary>Idle color of an interactive label (menu button / settings row).</summary>
        public static Color LabelColor => I._labelColor;
        /// <summary>Hover / focus / pressed color of an interactive label — the chosen blue (MainColor).</summary>
        public static Color LabelHighlightColor => I._mainColor;
        /// <summary>Greyed-out color of a non-interactable label.</summary>
        public static Color LabelColorDisabled => I._labelColorDisabled;

        public static Color ItemOutlineSelected => I._itemOutlineSelected;
        public static Color ItemOutlineHighlight => I._itemOutlineHighlight;
        public static Color ItemOutlineHighlightLocked => I._itemOutlineHighlightLocked;
        public static Color ItemOutlineIdle => I._itemOutlineIdle;
        public static Color ItemOutlineIdleLocked => I._itemOutlineIdleLocked;

        // UI Animation
        public static float PanelSlideDuration => I._panelSlideDuration;
        public static Ease PanelSlideInEase => I._panelSlideInEase;
        public static Ease PanelSlideOutEase => I._panelSlideOutEase;
        public static float HoverScale => I._hoverScale;
        public static float HoverDuration => I._hoverDuration;
        public static Ease HoverEase => I._hoverEase;

        /// <summary>
        /// Resolves the outline color for a shop list item from its interaction + content state,
        /// mirroring the amulet item's border logic. Priority: selected &gt; highlighted &gt; idle,
        /// each split by availability (unlocked / affordable).
        /// </summary>
        public static Color GetItemOutlineColor(bool isSelected, bool isHighlighted, bool isAvailable)
        {
            if (isSelected)
                return I._itemOutlineSelected;
            if (isHighlighted)
                return isAvailable ? I._itemOutlineHighlight : I._itemOutlineHighlightLocked;
            return isAvailable ? I._itemOutlineIdle : I._itemOutlineIdleLocked;
        }

        #endregion
    }
}