using System;

[AttributeUsage(AttributeTargets.Field)]
public class UIStatAttribute : Attribute
{
    public string DisplayName;
    public string Format;

    public UIStatAttribute(string displayName, string format = "{0}")
    {
        DisplayName = displayName;
        Format = format;
    }
}
