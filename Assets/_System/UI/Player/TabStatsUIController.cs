using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

public class TabStatsUIController : UIControllerBase
{
    // todo need view ??
    
    
    [Header("UI References")] public RectTransform PanelRect;
    public RectTransform StatsContainer;
    [FormerlySerializedAs("statUiuiPrefab")] [FormerlySerializedAs("StatUIPrefab")] public StatTabViewItem statPrefab;

    [Header("Animation Settings")] public Vector2 OffScreenPosition = new(-500, 0f);
    public Vector2 OnScreenPosition = new(0f, 0f);
    public float AnimationDuration = 0.25f;

    private EntityManager _entityManager;
    private EntityQuery _inputsEntityQuery;
    private EntityQuery _coreStatsQuery;

    private Coroutine _animationCoroutine;
    private bool _isViewOpen = false;

    private Dictionary<string, StatTabViewItem> _statUIs = new Dictionary<string, StatTabViewItem>();

    private struct StatFieldMeta
    {
        public FieldInfo Field;
        public UIStatAttribute Attribute;
    }

    private readonly List<StatFieldMeta> _cachedStatFields = new List<StatFieldMeta>();

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
    }

    private void Start()
    {
        _inputsEntityQuery = _entityManager.CreateEntityQuery(typeof(InputData));
        _coreStatsQuery = _entityManager.CreateEntityQuery(typeof(CoreStats), typeof(Player));

        PanelRect.anchoredPosition = OffScreenPosition;

        // clear
        foreach (Transform el in StatsContainer.transform)
        {
            Destroy(el.gameObject);
        }

        // Cache for reflexion
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

    private void Update()
    {
        if (_inputsEntityQuery.IsEmpty)
            return;

        InputData input = _inputsEntityQuery.GetSingleton<InputData>();

        if (input.IsTabPressed && !_isViewOpen)
        {
            StatsContainer.gameObject.SetActive(true);
            _isViewOpen = true;
            MoveTo(OnScreenPosition);

            if (!_coreStatsQuery.IsEmpty)
                Refresh(_coreStatsQuery.GetSingleton<CoreStats>());
        }
        else if (input.IsTabPressed && _isViewOpen)
        {
            _isViewOpen = false;
            MoveTo(OffScreenPosition);
        }
    }

    private void MoveTo(Vector2 targetPosition)
    {
        if (_animationCoroutine != null)
            StopCoroutine(_animationCoroutine);

        _animationCoroutine = StartCoroutine(AnimateView(targetPosition));
    }

    private IEnumerator AnimateView(Vector2 targetPosition)
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

        StatsContainer.gameObject.SetActive(_isViewOpen);
    }

    public void Refresh(CoreStats coreStats)
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
            statUI = Instantiate(statPrefab, StatsContainer);
            _statUIs.Add(label, statUI);
        }

        statUI.Refresh(label, formattedValue);
    }
}