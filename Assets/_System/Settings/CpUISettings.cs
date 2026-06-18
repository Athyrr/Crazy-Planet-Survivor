using PrimeTween;
using UnityEngine;

namespace _System.Settings
{
    /// <summary>
    /// Single source of truth for every UI color, tint and animation value in the game.
    ///
    /// Organization (top to bottom): a small hand-picked <b>Brand Palette</b> first, then the
    /// <b>semantic tokens</b> grouped by role (Surfaces, Text/Labels, Item Outline, Item Content
    /// States, Stat Semantics, Level Pips, Actions, Run Result, Upgrade Cards) and finally the
    /// <b>Animation</b> values.
    ///
    /// Coherence rule: every semantic token's default is anchored to a brand color (documented in
    /// its tooltip), so out of the box the same color codes recur across every shop and screen.
    /// Each token stays an overridable serialized field — a designer can diverge a single role in
    /// the inspector, but the defaults keep the whole UI on the brand palette.
    /// </summary>
    [CreateAssetMenu(fileName = "UISettings", menuName = "CPSettings/UISettings")]
    public class CpUISettings: CpSettings<CpUISettings>
    {
        // ======================================================================================
        // BRAND PALETTE — the only hand-picked colors. Everything else anchors to these.
        // ======================================================================================
        [Header("Brand Palette")]
        [Tooltip("Primary brand color (HUD accents, primary call-to-action).")]
        [SerializeField] private Color _mainColor = Color.dodgerBlue;
        [Tooltip("Primary brand — hover / focus / active variant.")]
        [SerializeField] private Color _mainColorOver;

        [Tooltip("Secondary brand color (supporting accents).")]
        [SerializeField] private Color _secondColor;
        [Tooltip("Secondary brand — hover / active variant.")]
        [SerializeField] private Color _secondColorOver;

        [Tooltip("Complementary / contrast accent (shop item labels, badges, selected outline).")]
        [SerializeField] private Color _complementaryColor;
        [Tooltip("Complementary — resting / idle variant.")]
        [SerializeField] private Color _complementaryColorOver;

        [Tooltip("Opacity multiplier applied to 'off' / dimmed elements.")]
        [SerializeField, Range(0, 1)] private float _offElementOpacity;

        // ======================================================================================
        // SURFACES — item background fills.
        // ======================================================================================
        [Header("Surfaces / Item Background")]
        [Tooltip("Background tint of a normal / not-yet-purchased shop item.")]
        [SerializeField] private Color _itemBackground = new Color(0.12f, 0.12f, 0.14f, 1f);
        [Tooltip("Background tint of a purchased / owned item — lighter, to read as 'acquired'.")]
        [SerializeField] private Color _itemBackgroundOwned = new Color(0.30f, 0.30f, 0.34f, 1f);
        [Tooltip("Background tint of a fully maxed item — distinct from 'owned' to read as 'complete'.")]
        [SerializeField] private Color _itemBackgroundMaxed = new Color(0.16f, 0.30f, 0.18f, 1f);

        // ======================================================================================
        // TEXT / LABELS — text-only interactive labels (menu buttons / settings rows).
        // ======================================================================================
        [Header("Text / Labels")]
        [Tooltip("Idle color of an interactive text label (menu button / settings row).")]
        [SerializeField] private Color _labelColor = new Color(0.6528f, 0.6528f, 0.6528f, 1f);
        [Tooltip("Color of a non-interactable (greyed-out) label.")]
        [SerializeField] private Color _labelColorDisabled = new Color(0.42f, 0.42f, 0.48f, 1f);

        // ======================================================================================
        // ITEM OUTLINE — shop list-item border, by interaction state × availability.
        // ======================================================================================
        [Header("Item Outline")]
        [Tooltip("Outline color of a shop list item when its purchase is committed (selected). " +
                 "Anchor: Brand Complementary (the 'this is the active one' accent).")]
        [SerializeField] private Color _itemOutlineSelected = new Color(1f, 0.545f, 0f, 1f);
        [Tooltip("Outline color when the item is hovered/focused and available (unlocked / affordable). " +
                 "Anchor: Brand Main-Over (hover accent).")]
        [SerializeField] private Color _itemOutlineHighlight = new Color(0.7547f, 0.6731f, 0.4592f, 1f);
        [Tooltip("Outline color when the item is hovered/focused but locked (not unlocked / can't afford). " +
                 "Anchor: Stat Malus (red = unavailable).")]
        [SerializeField] private Color _itemOutlineHighlightLocked = new Color(1f, 0.1373f, 0f, 1f);
        [Tooltip("Outline color when the item is idle and available. Anchor: Brand Main.")]
        [SerializeField] private Color _itemOutlineIdle = new Color(1f, 0.7412f, 0f, 1f);
        [Tooltip("Outline color when the item is idle and locked. Anchor: Stat Malus dimmed.")]
        [SerializeField] private Color _itemOutlineIdleLocked = new Color(0.9623f, 0.3788f, 0.286f, 1f);
        [Tooltip("Outline color when the item is fully maxed (and not hovered / selected). " +
                 "Anchor: 'complete / gain' green family. Used by leveled shops (meta-progression).")]
        [SerializeField] private Color _itemOutlineMaxed = new Color(0.2902f, 0.8706f, 0.502f, 1f);

        // ======================================================================================
        // ITEM CONTENT STATES — locked / maxed / active tint for the item label + icon + badge.
        // ======================================================================================
        [Header("Item Content State")]
        [Tooltip("Label / icon tint when the item's next level is unaffordable (locked).")]
        [SerializeField] private Color _itemLockedColor = new Color(0.45f, 0.45f, 0.45f, 1f);
        [Tooltip("Label / icon tint when the item is fully maxed. Anchor: 'complete/gain' green family.")]
        [SerializeField] private Color _itemMaxedColor = new Color(0.2f, 1f, 0.2f, 1f);
        [Tooltip("Icon tint when the item is active (hovered / focused / selected) or idle — full-bright.")]
        [SerializeField] private Color _itemIconActive = Color.white;
        [Tooltip("Color of the 'Equipped' badge text shown on an equipped / owned item. " +
                 "Anchor: Brand Complementary (matches the active accent).")]
        [SerializeField] private Color _equippedLabelColor = new Color(1f, 0.7412f, 0f, 1f);

        // ======================================================================================
        // STAT SEMANTICS — bonus (green) vs malus (red); drives StatsFormatUtils rich-text.
        // ======================================================================================
        [Header("Stat Semantics")]
        [Tooltip("Color of a positive stat / modifier value (bonus). Also used for zero.")]
        [SerializeField] private Color _statBonusColor = new Color(0.2902f, 0.8706f, 0.502f);   // #4ADE80
        [Tooltip("Color of a negative stat / modifier value (malus).")]
        [SerializeField] private Color _statMalusColor = new Color(0.9725f, 0.4431f, 0.4431f);  // #F87171

        // ======================================================================================
        // LEVEL PIPS / PROGRESS — meta-progression level pips and after-purchase previews.
        // ======================================================================================
        [Header("Level Pips / Progress")]
        [Tooltip("Pip color for an already-acquired level. Anchor: Brand Complementary (the active accent).")]
        [SerializeField] private Color _pipFilledColor = new Color(1f, 0.7412f, 0f, 1f);
        [Tooltip("Pip color for a not-yet-acquired level.")]
        [SerializeField] private Color _pipEmptyColor = new Color(0.25f, 0.25f, 0.28f, 1f);
        [Tooltip("Pip / preview-text color for the 'next level' a purchase would unlock. " +
                 "Anchor: Stat Bonus green (a gain preview).")]
        [SerializeField] private Color _pipPreviewColor = new Color(0.2902f, 0.8706f, 0.502f, 1f);
        [Tooltip("Pip color when the upgrade is fully maxed (applies to every pip). " +
                 "Anchor: 'complete / gain' green family.")]
        [SerializeField] private Color _pipMaxedColor = new Color(0.2902f, 0.8706f, 0.502f, 1f);

        // ======================================================================================
        // ACTION — shop action-button text.
        // ======================================================================================
        [Header("Action")]
        [Tooltip("Action-button text color when the action is a purchase (Buy). Anchor: Brand Main-Over.")]
        [SerializeField] private Color _purchaseTextColor = Color.yellow;
        [Tooltip("Action-button text color when the action is choose / equip. Anchor: Brand Main.")]
        [SerializeField] private Color _chooseTextColor = Color.white;

        // ======================================================================================
        // RUN RESULT — Game Over outcome text.
        // ======================================================================================
        [Header("Run Result (Game Over)")]
        [Tooltip("Result text on victory / success. Anchor: Brand Main-Over (gold celebration accent).")]
        [SerializeField] private Color _resultVictoryColor = new Color(1f, 0.7412f, 0f, 1f);
        [Tooltip("Result text on death / defeat. Anchor: Stat Malus red.")]
        [SerializeField] private Color _resultDefeatColor = new Color(0.9725f, 0.4431f, 0.4431f, 1f);
        [Tooltip("Result text on a neutral end (timeout / fallback). Anchor: disabled-label grey.")]
        [SerializeField] private Color _resultNeutralColor = new Color(0.42f, 0.42f, 0.48f, 1f);

        // ======================================================================================
        // UPGRADE CARDS — in-run level-up card titles + hover, by upgrade type.
        // ======================================================================================
        [Header("Upgrade Cards (Level-up)")]
        [Tooltip("Title color of a player-stat upgrade card. Anchor: neutral white.")]
        [SerializeField] private Color _upgradeStatTitleColor = Color.white;
        [Tooltip("Title color of a spell-unlock card. Anchor: Brand Complementary (a new acquisition).")]
        [SerializeField] private Color _upgradeUnlockTitleColor = new Color(1f, 0.545f, 0f, 1f);
        [Tooltip("Title color of a spell-upgrade card. Anchor: Stat Bonus green (an improvement).")]
        [SerializeField] private Color _upgradeSpellTitleColor = new Color(0.2902f, 0.8706f, 0.502f, 1f);
        [Tooltip("Card stat-label color while the card is hovered / focused. Anchor: Brand Main-Over.")]
        [SerializeField] private Color _upgradeCardHighlightColor = new Color(1f, 0.7412f, 0f, 1f);
        [Tooltip("Card stat-label color while idle. Anchor: neutral white.")]
        [SerializeField] private Color _upgradeCardIdleColor = Color.white;

        // ======================================================================================
        // ANIMATION — panel slide + hover scale tweens (no color).
        // ======================================================================================
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

        // --- Brand ---
        public static Color MainColor => I._mainColor;
        public static Color MainColorOver => I._mainColorOver;
        public static Color SecondColor => I._secondColor;
        public static Color SecondColorOver => I._secondColorOver;
        public static Color ComplementaryColor => I._complementaryColor;
        public static Color ComplementaryColorOver => I._complementaryColorOver;
        public static float OffElementOpacity => I._offElementOpacity;

        // --- Surfaces ---
        /// <summary>Background tint of a normal / not-yet-purchased shop item.</summary>
        public static Color ItemBackground => I._itemBackground;
        /// <summary>Background tint of a purchased / owned item (lighter).</summary>
        public static Color ItemBackgroundOwned => I._itemBackgroundOwned;
        /// <summary>Background tint of a fully maxed item.</summary>
        public static Color ItemBackgroundMaxed => I._itemBackgroundMaxed;

        // --- Text / Labels ---
        /// <summary>Idle color of an interactive label (menu button / settings row).</summary>
        public static Color LabelColor => I._labelColor;
        /// <summary>
        /// Hover / focus / pressed color of an interactive menu / settings label. This is the unified
        /// hover color for menu / settings labels and is aliased to <see cref="MainColorOver"/> so it
        /// always tracks the brand hover accent.
        /// NOTE: shop list items do NOT use this — their label color is resolved by
        /// <see cref="GetItemContentColor"/> (Complementary scheme).
        /// </summary>
        public static Color LabelHighlightColor => I._mainColorOver;
        /// <summary>Greyed-out color of a non-interactable label.</summary>
        public static Color LabelColorDisabled => I._labelColorDisabled;

        // --- Item Outline ---
        public static Color ItemOutlineSelected => I._itemOutlineSelected;
        public static Color ItemOutlineHighlight => I._itemOutlineHighlight;
        public static Color ItemOutlineHighlightLocked => I._itemOutlineHighlightLocked;
        public static Color ItemOutlineIdle => I._itemOutlineIdle;
        public static Color ItemOutlineIdleLocked => I._itemOutlineIdleLocked;
        public static Color ItemOutlineMaxed => I._itemOutlineMaxed;

        // --- Item Content State ---
        /// <summary>Label/icon tint when the item's next level is unaffordable (locked).</summary>
        public static Color ItemLockedColor => I._itemLockedColor;
        /// <summary>Label/icon tint when the item is fully maxed.</summary>
        public static Color ItemMaxedColor => I._itemMaxedColor;
        /// <summary>Icon tint when the item is active (hovered/focused/selected) or idle — full-bright.</summary>
        public static Color ItemIconActive => I._itemIconActive;
        /// <summary>Color of the 'Equipped' badge text shown on an equipped item.</summary>
        public static Color EquippedLabelColor => I._equippedLabelColor;

        // --- Stat Semantics ---
        /// <summary>Color of a positive stat / modifier value (bonus). Also used for zero.</summary>
        public static Color StatBonusColor => I._statBonusColor;
        /// <summary>Color of a negative stat / modifier value (malus).</summary>
        public static Color StatMalusColor => I._statMalusColor;

        // --- Level Pips / Progress ---
        /// <summary>Pip color for an already-acquired level.</summary>
        public static Color PipFilledColor => I._pipFilledColor;
        /// <summary>Pip color for a not-yet-acquired level.</summary>
        public static Color PipEmptyColor => I._pipEmptyColor;
        /// <summary>Pip / preview-text color for the next level a purchase would unlock.</summary>
        public static Color PipPreviewColor => I._pipPreviewColor;
        /// <summary>Rich-text hex (e.g. "#4ADE80") of <see cref="PipPreviewColor"/> for inline color tags.</summary>
        public static string PipPreviewColorHex => "#" + ColorUtility.ToHtmlStringRGB(I._pipPreviewColor);
        /// <summary>Pip color when the upgrade is fully maxed (every pip).</summary>
        public static Color PipMaxedColor => I._pipMaxedColor;

        // --- Action ---
        /// <summary>Action-button text color when the action is a purchase (Buy).</summary>
        public static Color PurchaseTextColor => I._purchaseTextColor;
        /// <summary>Action-button text color when the action is choose / equip.</summary>
        public static Color ChooseTextColor => I._chooseTextColor;

        // --- Run Result ---
        /// <summary>Game-over result text color on victory / success.</summary>
        public static Color ResultVictoryColor => I._resultVictoryColor;
        /// <summary>Game-over result text color on death / defeat.</summary>
        public static Color ResultDefeatColor => I._resultDefeatColor;
        /// <summary>Game-over result text color on a neutral end (timeout / fallback).</summary>
        public static Color ResultNeutralColor => I._resultNeutralColor;

        // --- Upgrade Cards ---
        /// <summary>Title color of a player-stat upgrade card.</summary>
        public static Color UpgradeStatTitleColor => I._upgradeStatTitleColor;
        /// <summary>Title color of a spell-unlock upgrade card.</summary>
        public static Color UpgradeUnlockTitleColor => I._upgradeUnlockTitleColor;
        /// <summary>Title color of a spell-upgrade card.</summary>
        public static Color UpgradeSpellTitleColor => I._upgradeSpellTitleColor;
        /// <summary>Upgrade card stat-label color while hovered / focused.</summary>
        public static Color UpgradeCardHighlightColor => I._upgradeCardHighlightColor;
        /// <summary>Upgrade card stat-label color while idle.</summary>
        public static Color UpgradeCardIdleColor => I._upgradeCardIdleColor;

        // --- Animation ---
        public static float PanelSlideDuration => I._panelSlideDuration;
        public static Ease PanelSlideInEase => I._panelSlideInEase;
        public static Ease PanelSlideOutEase => I._panelSlideOutEase;
        public static float HoverScale => I._hoverScale;
        public static float HoverDuration => I._hoverDuration;
        public static Ease HoverEase => I._hoverEase;

        #region Resolvers

        /// <summary>
        /// Resolves the outline color for a shop list item from its interaction + content state,
        /// shared by every shop item (amulet / character / meta). Priority: selected &gt; highlighted
        /// &gt; idle, each split by availability (unlocked / affordable). Binary shops use this overload.
        /// </summary>
        public static Color GetItemOutlineColor(bool isSelected, bool isHighlighted, bool isAvailable)
            => GetItemOutlineColor(isSelected, isHighlighted, isAvailable, isMaxed: false);

        /// <summary>
        /// Outline resolver with a maxed branch for leveled shops (meta-progression). Priority:
        /// selected &gt; highlighted (split by availability) &gt; maxed &gt; idle (split by availability),
        /// so an interacted maxed item still shows the interaction outline and the maxed outline only
        /// appears at rest.
        /// </summary>
        public static Color GetItemOutlineColor(bool isSelected, bool isHighlighted, bool isAvailable, bool isMaxed)
        {
            if (isSelected)
                return I._itemOutlineSelected;
            if (isHighlighted)
                return isAvailable ? I._itemOutlineHighlight : I._itemOutlineHighlightLocked;
            if (isMaxed)
                return I._itemOutlineMaxed;
            return isAvailable ? I._itemOutlineIdle : I._itemOutlineIdleLocked;
        }

        /// <summary>
        /// Resolves a shop list-item's <b>name label</b> color from its combined interaction + content
        /// state, shared by every shop item so the priority is defined once.
        /// Priority: active (selected or highlighted) &gt; maxed &gt; locked &gt; idle.
        /// Binary shops (amulet / character) pass <paramref name="isMaxed"/> = <paramref name="isLocked"/> =
        /// false and so only ever see the active / idle Complementary colors.
        /// </summary>
        public static Color GetItemContentColor(bool isActive, bool isMaxed, bool isLocked)
        {
            if (isActive)
                return I._complementaryColor;
            if (isMaxed)
                return I._itemMaxedColor;
            if (isLocked)
                return I._itemLockedColor;
            return I._complementaryColorOver;
        }

        /// <summary>
        /// Resolves a shop list-item's <b>icon</b> tint, mirroring <see cref="GetItemContentColor"/> but
        /// with the active/idle states full-bright (<see cref="ItemIconActive"/>) rather than tinted.
        /// Priority: active &gt; maxed &gt; locked &gt; idle.
        /// </summary>
        public static Color GetItemIconColor(bool isActive, bool isMaxed, bool isLocked)
        {
            if (isActive)
                return I._itemIconActive;
            if (isMaxed)
                return I._itemMaxedColor;
            if (isLocked)
                return I._itemLockedColor;
            return I._itemIconActive;
        }

        #endregion

        #endregion
    }
}
