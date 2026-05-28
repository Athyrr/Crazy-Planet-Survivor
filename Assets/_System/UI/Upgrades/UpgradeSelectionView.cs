using System;
using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

public class UpgradeSelectionView : UIViewBase
{
    public event Action<int> OnUpgradeSelected;

    [Header("References")] public Transform UpgradesContainer;
    public UpgradeViewItem UpgradePrefab;

    [Header("Layout Settings")] public float Spacing = 3.5f;
    public float ArcHeight = 0.5f;
    public float RotationAmount = 10f;

    [Header("Animation Settings")] public float DelayBetweenCards = 0.15f;
    public float PopDuration = 0.4f;
    public AnimationCurve PopCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private List<UpgradeViewItem> _spawnedUpgradeItems = new List<UpgradeViewItem>();
    private UpgradeViewItem _currentHoveredCard;
    private int _focusedIndex = -1;
    private bool _canInteract = false;
    private bool _navAxisActive = false;
    private Coroutine _animationCoroutine;
    private GameInputs _inputs;

    private void OnEnable()
    {
        if (_inputs == null)
            _inputs = new GameInputs();

        _inputs.UI.Navigate.performed += OnNavigate;
        _inputs.UI.Submit.performed += OnSubmit;
        _inputs.UI.Enable();
    }

    private void OnDisable()
    {
        if (_inputs == null)
            return;

        _inputs.UI.Navigate.performed -= OnNavigate;
        _inputs.UI.Submit.performed -= OnSubmit;
        _inputs.UI.Disable();
    }

    public void ClearSelection()
    {
        StopAllCoroutines();

        foreach (var item in _spawnedUpgradeItems)
        {
            if (item == null)
                continue;
            item.PointerEntered -= HandleItemPointerEntered;
            item.PointerExited -= HandleItemPointerExited;
            item.PointerClicked -= HandleItemPointerClicked;
        }

        foreach (Transform child in UpgradesContainer)
            Destroy(child.gameObject);

        _spawnedUpgradeItems.Clear();
        _currentHoveredCard = null;
        _focusedIndex = -1;
        _navAxisActive = false;
    }

    private void ApplyLayout(List<Transform> cards)
    {
        int count = cards.Count;
        if (count == 0) return;

        float totalWidth = (count - 1) * Spacing;
        float startX = -totalWidth / 2f;

        for (int i = 0; i < count; i++)
        {
            Transform card = cards[i];

            // Calcul Position
            float xPos = startX + (i * Spacing);
            float normalizedPos = count > 1 ? (float)i / (count - 1) : 0.5f;

            float xSym = (normalizedPos - 0.5f) * 2f; // -1 : 1
            float yPos = -Mathf.Abs(xSym) * ArcHeight;
            float rotZ = -xSym * RotationAmount;

            var uiComp = card.GetComponent<UpgradeViewItem>();
            if (uiComp != null)
                uiComp.SetInitialPosition(new Vector3(xPos, yPos, 0));

            card.localPosition = new Vector3(xPos, yPos, 0);
            card.localRotation = Quaternion.Euler(-90, -180, rotZ);
        }
    }

    private IEnumerator AnimateCardsEntry()
    {
        foreach (var card in _spawnedUpgradeItems)
        {
            StartCoroutine(AnimateSingleCardPop(card.transform));
            yield return new WaitForSecondsRealtime(DelayBetweenCards);
        }

        _canInteract = true;
        _animationCoroutine = null;

        if (_focusedIndex < 0 && _spawnedUpgradeItems.Count > 0)
            SetFocusedIndex(_spawnedUpgradeItems.Count / 2);
    }

    private IEnumerator AnimateSingleCardPop(Transform target)
    {
        float elapsed = 0f;
        Vector3 finalScale = Vector3.one;

        while (elapsed < PopDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / PopDuration;
            float scaleValue = PopCurve.Evaluate(t);

            if (target != null)
                target.localScale = finalScale * scaleValue;

            yield return null;
        }

        if (target != null)
            target.localScale = finalScale;
    }


    public void SpawnAndLayoutCards(List<int> indices, ref BlobArray<UpgradeBlob> upgradesDatabase)
    {
        gameObject.SetActive(true);
        ClearSelection();

        List<Transform> cardsTransforms = new List<Transform>();

        for (var i = 0; i < indices.Count; i++)
        {
            var indice = indices[i];
            ref UpgradeBlob upgradeData = ref upgradesDatabase[indice];

            var upgradeViewItem = Instantiate(UpgradePrefab, UpgradesContainer);
            _spawnedUpgradeItems.Add(upgradeViewItem);
            cardsTransforms.Add(upgradeViewItem.transform);

            upgradeViewItem.SetData(ref upgradeData, indice);
            upgradeViewItem.PointerEntered += HandleItemPointerEntered;
            upgradeViewItem.PointerExited += HandleItemPointerExited;
            upgradeViewItem.PointerClicked += HandleItemPointerClicked;

            upgradeViewItem.transform.localScale = Vector3.zero;
        }

        ApplyLayout(cardsTransforms);
        StartCoroutine(AnimateCardsEntry());
    }


    private void HandleItemPointerEntered(UpgradeViewItem item)
    {
        if (!_canInteract)
            return;

        int idx = _spawnedUpgradeItems.IndexOf(item);
        if (idx >= 0)
            SetFocusedIndex(idx);
    }

    private void HandleItemPointerExited(UpgradeViewItem item)
    {
        if (!_canInteract || _currentHoveredCard != item)
            return;

        _currentHoveredCard.SetHovered(false);
        _currentHoveredCard = null;
        _focusedIndex = -1;
    }

    private void HandleItemPointerClicked(UpgradeViewItem item)
    {
        if (!_canInteract)
            return;

        ConfirmSelection(item);
    }

    private void OnNavigate(InputAction.CallbackContext ctx)
    {
        if (!_canInteract || _spawnedUpgradeItems.Count == 0)
            return;

        Vector2 v = ctx.ReadValue<Vector2>();
        float absX = Mathf.Abs(v.x);

        // Debounce so a held stick/key only steps once per actuation.
        if (absX < 0.3f)
        {
            _navAxisActive = false;
            return;
        }
        if (_navAxisActive)
            return;
        _navAxisActive = true;

        int step = v.x > 0 ? 1 : -1;
        int n = _spawnedUpgradeItems.Count;
        int start = _focusedIndex < 0 ? 0 : _focusedIndex;
        int next = ((start + step) % n + n) % n;
        SetFocusedIndex(next);
    }

    private void OnSubmit(InputAction.CallbackContext ctx)
    {
        if (!_canInteract || _currentHoveredCard == null)
            return;

        ConfirmSelection(_currentHoveredCard);
    }

    private void SetFocusedIndex(int idx)
    {
        if (idx < 0 || idx >= _spawnedUpgradeItems.Count)
            return;

        _focusedIndex = idx;
        UpgradeViewItem card = _spawnedUpgradeItems[idx];

        if (_currentHoveredCard == card)
            return;

        if (_currentHoveredCard != null)
            _currentHoveredCard.SetHovered(false);

        card.SetHovered(true);
        _currentHoveredCard = card;
    }

    private void ConfirmSelection(UpgradeViewItem card)
    {
        int selectedDbIndex = card.DbIndex;
        _canInteract = false;

        Tween.Scale(card.transform, endValue: 1.2f, duration: 0.2f)
            .OnComplete(() => OnUpgradeSelected?.Invoke(selectedDbIndex));
    }
}