using System.Text.RegularExpressions;

namespace ProductionIncident.Mcp.Tools;

/// <summary>Server-side validation of tool inputs (security principle 5). Never trust model-generated arguments.</summary>
public static partial class ToolInput
{
    [GeneratedRegex("^[a-z0-9][a-z0-9\\-]{1,62}$")]
    private static partial Regex NameRegex();

    [GeneratedRegex("^[A-Za-z0-9\\-_.]{1,64}$")]
    private static partial Regex IdRegex();

    [GeneratedRegex("^v?[0-9A-Za-z.\\-]{1,32}$")]
    private static partial Regex VersionRegex();

    [GeneratedRegex("^[A-Za-z0-9:_.\\-]{1,128}$")]
    private static partial Regex ConfigKeyRegex();

    public static string Service(string? value, string parameter = "service")
    {
        var v = value?.Trim().ToLowerInvariant();
        if (v is null || !NameRegex().IsMatch(v))
        {
            throw new ArgumentException($"Invalid {parameter} '{value}'. Expected a lowercase service/resource name like 'checkout-api'.", parameter);
        }

        return v;
    }

    public static string Id(string? value, string parameter)
    {
        var v = value?.Trim();
        if (v is null || !IdRegex().IsMatch(v))
        {
            throw new ArgumentException($"Invalid {parameter} '{value}'.", parameter);
        }

        return v;
    }

    public static string Version(string? value, string parameter)
    {
        var v = value?.Trim();
        if (v is null || !VersionRegex().IsMatch(v))
        {
            throw new ArgumentException($"Invalid {parameter} '{value}'.", parameter);
        }

        return v;
    }

    public static string ConfigKey(string? value)
    {
        var v = value?.Trim();
        if (v is null || !ConfigKeyRegex().IsMatch(v))
        {
            throw new ArgumentException($"Invalid configuration key '{value}'.", "key");
        }

        return v;
    }

    public static string Text(string? value, string parameter, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameter} is required.", parameter);
        }

        return value.Length > maxLength ? value[..maxLength] : value;
    }

    public static int Window(int value, int min, int max) => Math.Clamp(value, min, max);
}
