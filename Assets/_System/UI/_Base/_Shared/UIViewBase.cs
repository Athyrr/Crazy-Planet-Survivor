using System;
using UnityEngine;

public abstract class UIViewBase : MonoBehaviour
{
    public event Action<UIViewBase> OnOpen;
    public event Action<UIViewBase> OnClose;

    protected virtual void Start(){}

    public virtual void OpenView()
    {
        OnOpen?.Invoke(this);
        // gameObject.SetActive(true);
    }

    public virtual void CloseView()
    {
        OnClose?.Invoke(this);
        // gameObject.SetActive(false);
    }
}