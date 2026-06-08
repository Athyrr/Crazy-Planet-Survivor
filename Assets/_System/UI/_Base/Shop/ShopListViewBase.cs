using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class ShopListViewBase<TItem> : UIViewBase where TItem : UIViewItemBase
{
    [Header("List Settings")] public Transform Container;
    public TItem ItemPrefab;

    private List<TItem> _activeItems = new List<TItem>();

    protected virtual void Awake()
    {
        foreach (Transform child in Container)
            Destroy(child.gameObject);
    }

    public virtual void Clear()
    {
        foreach (var item in _activeItems)
        {
            if (item == null)
                continue;

            item.gameObject.SetActive(false);
        }

        _activeItems.Clear();
    }

    protected internal TItem GetOrCreateItem()
    {
        foreach (Transform child in Container)
        {
            if (!child.gameObject.activeSelf && child.TryGetComponent<TItem>(out var item))
            {
                item.gameObject.SetActive(true);
                _activeItems.Add(item);
                return item;
            }
        }

        TItem newItem = Instantiate(ItemPrefab, Container);
        _activeItems.Add(newItem);
        return newItem;
    }

    /// <summary>Pointer hover highlight (PC). Pass -1 to clear hover on every item.</summary>
    public void SetHovered(int index)
    {
        for (int i = 0; i < _activeItems.Count; i++)
            _activeItems[i].SetHovered(i == index);
    }

    public void SetFocused(int index)
    {
        for (int i = 0; i < _activeItems.Count; i++)
            _activeItems[i].SetFocus(i == index);
    }

    public void SetSelected(int index)
    {
        for (int i = 0; i < _activeItems.Count; i++)
            _activeItems[i].SetSelected(i == index);
    }
}