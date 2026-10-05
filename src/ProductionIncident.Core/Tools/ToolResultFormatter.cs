using System.Text;
using System.Text.Json;
using ProductionIncident.Core.Json;

namespace ProductionIncident.Core.Tools;

/// <summary>Turns whatever an AIFunction returned (object, JsonElement, MCP CallToolResult) into text for the model.</summary>
public static class ToolResultFormatter
{
    public static string ToText(object? raw)
    {
        switch (raw)
        {
            case null:
                return "null";
            case string s:
                return s;
            case JsonElement element:
                return FromElement(element);
            default:
                var serialized = JsonSerializer.SerializeToElement(raw, JsonDefaults.Options);
                return FromElement(serialized);
        }
    }

    private static string FromElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? "";
        }

        // MCP CallToolResult shape: { "content": [ { "type": "text", "text": "..." } ], "isError": bool }
        if (element.ValueKind == JsonValueKind.Object &&
            TryGet(element, "content", out var content) &&
            content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.Object &&
                    TryGet(block, "text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append('\n');
                    }

                    sb.Append(text.GetString());
                }
            }

            var isError = TryGet(element, "isError", out var err) && err.ValueKind == JsonValueKind.True;
            if (sb.Length > 0)
            {
                return isError ? $"Error: {sb}" : sb.ToString();
            }
        }

        return element.GetRawText();
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
