using System;
using Unity.Collections;

[AttributeUsage(AttributeTargets.Field)]
public class UIStatAttribute : Attribute
{
    public string DisplayName;
    public string Format;
    public float NeutralValue;

    public UIStatAttribute(string displayName, string format = "{0}", float neutralValue = 0f)
    {
        DisplayName = displayName;
        Format = format;
        NeutralValue = neutralValue;
    }
}
