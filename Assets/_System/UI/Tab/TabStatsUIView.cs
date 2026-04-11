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

    protected override void Start()
    {
        base.Start();

        PanelRect.anchoredPosition = OffScreenPosition;
        StatsContainer.gameObject.SetActive(false);

        foreach (Transform el in StatsContainer.transform)
            Destroy(el.gameObject);

        Type type = typeof(CoreStats);
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
        foreach (var meta in _cachedStatFields)
        {
            var fieldValue = meta.Field.GetValue(coreStats);
            string displayName = meta.Attribute.DisplayName;
            string format = meta.Attribute.Format;

            string formattedValue = string.Format(format, fieldValue);
            UpdateStat(displayName, formattedValue);
        }
    }

    private void UpdateStat(string label, string formattedValue)
    {
        if (!_statUIs.TryGetValue(label, out StatTabViewItem statUI))
        {
            statUI = Instantiate(TabStatItemPrefab, StatsContainer);
            _statUIs.Add(label, statUI);
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