using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ProductionIncident.Core.Json;

/// <summary>
/// Tolerant parsing of model output into structured contracts:
/// strips markdown fences / prose and extracts the outermost JSON object.
/// </summary>
public static class LlmJson
{
    public static bool TryParse<T>(string? text, [NotNullWhen(true)] out T? value, out string? error)
        where T : class
    {
        value = null;
        error = null;

        var json = ExtractObject(text);
        if (json is null)
        {
            error = "No JSON object found in model output.";
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(json, JsonDefaults.Options);
            if (value is null)
            {
                error = "JSON deserialized to null.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON for {typeof(T).Name}: {ex.Message}";
            return false;
        }
    }

    public static string? ExtractObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return text[start..(i + 1)];
                    }

                    break;
            }
        }

        return null;
    }

    /// <summary>Converts a tool/function argument value (string, JsonElement, number...) to string.</summary>
    public static string? ArgToString(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement e => e.GetRawText(),
        _ => value.ToString(),
    };
}
