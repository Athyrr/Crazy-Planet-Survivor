using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

public static class ConsoleCommandInvoker
{
    public readonly struct InvokeResult
    {
        public readonly bool Success;
        public readonly string Output;
        public InvokeResult(bool success, string output) { Success = success; Output = output; }
    }

    public static InvokeResult Execute(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
            return new InvokeResult(false, string.Empty);

        ConsoleCommandRegistry.EnsureInitialized();

        var tokens = Tokenize(rawLine);
        if (tokens.Count == 0)
            return new InvokeResult(false, string.Empty);

        var name = tokens[0];
        if (!ConsoleCommandRegistry.TryGet(name, out var entry))
            return new InvokeResult(false, $"Unknown command '{name}'. Type 'help' for the list.");

        var rawArgs = tokens.GetRange(1, tokens.Count - 1);
        var args = new object[entry.Parameters.Length];

        for (int i = 0; i < entry.Parameters.Length; i++)
        {
            var p = entry.Parameters[i];
            if (i < rawArgs.Count)
            {
                if (!TryConvert(rawArgs[i], p.ParameterType, out args[i], out var err))
                    return new InvokeResult(false, $"Argument '{p.Name}': {err}\nUsage: {entry.Usage}");
            }
            else if (p.HasDefaultValue)
            {
                args[i] = p.DefaultValue;
            }
            else
            {
                return new InvokeResult(false, $"Missing argument '{p.Name}'.\nUsage: {entry.Usage}");
            }
        }

        try
        {
            var ret = entry.Method.Invoke(null, args);
            return new InvokeResult(true, ret == null ? string.Empty : ret.ToString());
        }
        catch (TargetInvocationException tie)
        {
            var inner = tie.InnerException ?? tie;
            return new InvokeResult(false, $"Command threw: {inner.GetType().Name}: {inner.Message}");
        }
        catch (Exception ex)
        {
            return new InvokeResult(false, $"Invocation error: {ex.Message}");
        }
    }

    private static bool TryConvert(string raw, Type targetType, out object value, out string error)
    {
        error = null;
        value = null;

        try
        {
            if (targetType == typeof(string)) { value = raw; return true; }
            if (targetType == typeof(int)) { value = int.Parse(raw, CultureInfo.InvariantCulture); return true; }
            if (targetType == typeof(float)) { value = float.Parse(raw, CultureInfo.InvariantCulture); return true; }
            if (targetType == typeof(double)) { value = double.Parse(raw, CultureInfo.InvariantCulture); return true; }
            if (targetType == typeof(bool))
            {
                if (raw.Equals("1") || raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Equals("on", StringComparison.OrdinalIgnoreCase) || raw.Equals("yes", StringComparison.OrdinalIgnoreCase))
                { value = true; return true; }
                if (raw.Equals("0") || raw.Equals("false", StringComparison.OrdinalIgnoreCase) || raw.Equals("off", StringComparison.OrdinalIgnoreCase) || raw.Equals("no", StringComparison.OrdinalIgnoreCase))
                { value = false; return true; }
                error = "expected bool (true/false)"; return false;
            }
            if (targetType.IsEnum)
            {
                value = Enum.Parse(targetType, raw, ignoreCase: true);
                return true;
            }
        }
        catch (Exception ex)
        {
            error = $"could not parse '{raw}' as {targetType.Name} ({ex.Message})";
            return false;
        }

        error = $"unsupported parameter type {targetType.Name}";
        return false;
    }

    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }
}
