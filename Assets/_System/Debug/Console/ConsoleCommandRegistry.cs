using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class ConsoleCommandRegistry
{
    public sealed class Entry
    {
        public string Name;
        public string Description;
        public MethodInfo Method;
        public ParameterInfo[] Parameters;

        public string Usage
        {
            get
            {
                if (Parameters.Length == 0) return Name;
                var parts = Parameters.Select(p =>
                    p.HasDefaultValue
                        ? $"[{p.Name}:{FriendlyTypeName(p.ParameterType)}]"
                        : $"<{p.Name}:{FriendlyTypeName(p.ParameterType)}>");
                return Name + " " + string.Join(" ", parts);
            }
        }

        private static string FriendlyTypeName(Type t)
        {
            if (t == typeof(int)) return "int";
            if (t == typeof(float)) return "float";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            return t.Name;
        }
    }

    private static readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    public static IEnumerable<Entry> All => _entries.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string name, out Entry entry) => _entries.TryGetValue(name, out entry);

    public static List<Entry> FindMatches(string prefix)
    {
        EnsureInitialized();
        if (string.IsNullOrEmpty(prefix))
            return All.ToList();
        return _entries.Values
            .Where(e => e.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        Scan();
    }

    private static void Scan()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ShouldSkip(asm)) continue;

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            catch { continue; }

            foreach (var type in types)
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly); }
                catch { continue; }

                foreach (var method in methods)
                {
                    var attr = method.GetCustomAttribute<ConsoleCommandAttribute>();
                    if (attr == null) continue;

                    if (_entries.ContainsKey(attr.Name))
                    {
                        Debug.LogWarning($"[ConsoleCommand] Duplicate command name '{attr.Name}' on {type.FullName}.{method.Name}. Ignored.");
                        continue;
                    }

                    _entries[attr.Name] = new Entry
                    {
                        Name = attr.Name,
                        Description = attr.Description,
                        Method = method,
                        Parameters = method.GetParameters(),
                    };
                }
            }
        }
    }

    private static bool ShouldSkip(Assembly asm)
    {
        var name = asm.GetName().Name;
        if (string.IsNullOrEmpty(name)) return true;
        if (name.StartsWith("Unity")) return true;
        if (name.StartsWith("UnityEditor")) return true;
        if (name.StartsWith("UnityEngine")) return true;
        if (name.StartsWith("System")) return true;
        if (name.StartsWith("Mono")) return true;
        if (name.StartsWith("netstandard")) return true;
        if (name.StartsWith("mscorlib")) return true;
        if (name.StartsWith("nunit")) return true;
        if (name.StartsWith("PrimeTween")) return true;
        if (name.StartsWith("EasyButtons")) return true;
        return false;
    }
}
