using System;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ConsoleCommandAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public ConsoleCommandAttribute(string name, string description = "")
    {
        Name = name;
        Description = description;
    }
}
