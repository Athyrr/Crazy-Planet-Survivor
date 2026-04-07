using System;
using UnityEngine;

public abstract class UIViewBase : MonoBehaviour
{
    public event Action<UIViewBase> OnOpenStart;
    public event Action<UIViewBase> OnCloseStart;

    protected virtual void Start(){}

    public virtual void OpenView()
    {
        OnOpenStart?.Invoke(this);
        gameObject.SetActive(true);
    }

    public virtual void CloseView()
    {
        OnCloseStart?.Invoke(this);
        gameObject.SetActive(false);
    }
}