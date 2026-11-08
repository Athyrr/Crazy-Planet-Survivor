using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class TabStatsUIView : UIViewBase
{
    [Header("UI References")] public RectTransform PanelRect;
    public RectTransform StatsContainer;
    public StatTabViewItem TabStatItemPrefab;

    [Header("Animation Settings")] public Vector2 OffScreenPosition = new Vector2(-500, 0f);
    public Vector2 OnScreenPosition = new Vector2(0f, 0f);
    public float AnimationDuration = 0.25f;

    private Coroutine _animationCoroutine;
    public bool IsOpen { get; private set; }

    private Dictionary<string, StatTabViewItem> _statUIs = new Dictionary<string, StatTabViewItem>();

    private struct StatFieldMeta
    {
        public FieldInfo Field;
        public UIStatAttribute Attribute;
    }

    private readonly List<StatFieldMeta> _cachedStatFields = new List<StatFieldMeta>();

    private bool _initialized;

    private readonly Dictionary<ECharacterStat, StatTabViewItem> _statToRow =
        new Dictionary<ECharacterStat, StatTabViewItem>();

    private readonly HashSet<ECharacterStat> _highlightedStats = new HashSet<ECharacterStat>();
    private UpgradeSelectionView _selectionView;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnEnable()
    {
        TrySubscribeSelection();
    }

    private void OnDisable()
    {
        if (_selectionView != null)
            _selectionView.HighlightChanged -= OnHighlightChanged;
        _selectionView = null;
    }

    // The selection view lives in another prefab: find it lazily (also retried on OpenView).
    private void TrySubscribeSelection()
    {
        if (_selectionView != null)
            return;

        _selectionView = FindFirstObjectByType<UpgradeSelectionView>(FindObjectsInactive.Include);
        if (_selectionView != null)
            _selectionView.HighlightChanged += OnHighlightChanged;
    }

    private void OnHighlightChanged(UpgradeHighlightTargets targets) => SetHighlightedStats(targets.Stats);

    /// <summary>Highlights the rows of <paramref name="stats"/>, un-highlights the others. Rows created later honor it.</summary>
    public void SetHighlightedStats(HashSet<ECharacterStat> stats)
    {
        _highlightedStats.Clear();
        if (stats != null)
            _highlightedStats.UnionWith(stats);

        foreach (var pair in _statToRow)
            pair.Value.SetHighlighted(_highlightedStats.Contains(pair.Key));
    }
    
    private void EnsureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;

        PanelRect.anchoredPosition = OffScreenPosition;
        StatsContainer.gameObject.SetActive(false);

        foreach (Transform el in StatsContainer.transform)
            Destroy(el.gameObject);

        var type = typeof(CoreStats);
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
        foreach (var field in fields)
        {
            var fieldAttribute = field.GetCustomAttribute<UIStatAttribute>();
            if (fieldAttribute != null)
            {
                _cachedStatFields.Add(new StatFieldMeta { Field = field, Attribute = fieldAttribute });
            }
        }
    }

    public override void OpenView()
    {
        EnsureInitialized();
        TrySubscribeSelection();

        IsOpen = true;
        gameObject.SetActive(true);
        StatsContainer.gameObject.SetActive(true);
        AnimateView(OnScreenPosition);
    }

    public override void CloseView()
    {
        base.CloseView();

        IsOpen = false;
        AnimateView(OffScreenPosition);
    }

    public void RefreshData(CoreStats coreStats)
    {
        EnsureInitialized();

        foreach (var meta in _cachedStatFields)
        {
            var attr = meta.Attribute;
            float rawValue = System.Convert.ToSingle(meta.Field.GetValue(coreStats));

            string formattedValue = StatsFormatUtils.FormatPanelStat(
                rawValue, attr.Stat, attr.NeutralValue, attr.Absolute, attr.Suffix, attr.Decimals);
            UpdateStat(attr.DisplayName, attr.Stat, formattedValue);
        }
    }

    private void UpdateStat(string label, ECharacterStat stat, string formattedValue)
    {
        if (!_statUIs.TryGetValue(label, out StatTabViewItem statUI))
        {
            statUI = Instantiate(TabStatItemPrefab, StatsContainer);
            _statUIs.Add(label, statUI);
            _statToRow[stat] = statUI;
            if (_highlightedStats.Contains(stat))
                statUI.SetHighlighted(true);
        }

        statUI.Refresh(label, formattedValue);
    }

    private void AnimateView(Vector2 targetPosition)
    {
        if (_animationCoroutine != null)
            StopCoroutine(_animationCoroutine);

        if (!gameObject.activeInHierarchy)
            gameObject.SetActive(true);

        _animationCoroutine = StartCoroutine(AnimateViewCoroutine(targetPosition));
    }

    private IEnumerator AnimateViewCoroutine(Vector2 targetPosition)
    {
        Vector2 startPosition = PanelRect.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < AnimationDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / AnimationDuration);
            float easeT = 1f - (1f - t) * (1f - t); // ease out

            PanelRect.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, easeT);
            yield return null;
        }

        PanelRect.anchoredPosition = targetPosition;
        _animationCoroutine = null;
    }
}