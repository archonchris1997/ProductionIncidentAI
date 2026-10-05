using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ProductionIncident.Harness.Context;

/// <summary>
/// Tool-result trimming (blueprint §13.3): large results are cut on arrival, and once consumed,
/// older results are replaced by a short preview + a workspace reference to the full content.
/// </summary>
public sealed class ToolResultTrimmer(IOptions<HarnessOptions> options)
{
    private const string TrimmedMarker = "[trimmed:";
    private readonly HarnessOptions _options = options.Value;

    public string TrimOnArrival(string result, string workspaceReference)
    {
        if (result.Length <= _options.MaxToolResultChars)
        {
            return result;
        }

        var head = result[.._options.MaxToolResultChars];
        return $"{head}\n...[truncated {result.Length - _options.MaxToolResultChars} chars; full result: {workspaceReference}]";
    }

    /// <summary>
    /// Replaces all but the most recent <see cref="HarnessOptions.KeepRecentToolResults"/> tool results with previews.
    /// Returns the number of results trimmed.
    /// </summary>
    public int TrimHistory(IList<ChatMessage> messages, IReadOnlyDictionary<string, string> references)
    {
        var positions = new List<(ChatMessage Message, int Index, FunctionResultContent Content)>();
        foreach (var message in messages)
        {
            for (var i = 0; i < message.Contents.Count; i++)
            {
                if (message.Contents[i] is FunctionResultContent frc)
                {
                    positions.Add((message, i, frc));
                }
            }
        }

        var toTrim = positions.Count - _options.KeepRecentToolResults;
        var trimmed = 0;
        for (var p = 0; p < toTrim; p++)
        {
            var (message, index, content) = positions[p];
            var text = content.Result?.ToString() ?? "";
            if (text.StartsWith(TrimmedMarker, StringComparison.Ordinal) || text.Length <= _options.TrimmedPreviewChars)
            {
                continue;
            }

            var reference = references.GetValueOrDefault(content.CallId, "n/a");
            var preview = text[.._options.TrimmedPreviewChars];
            message.Contents[index] = new FunctionResultContent(
                content.CallId,
                $"{TrimmedMarker} {text.Length} chars, already analysed; ref={reference}] {preview}...");
            trimmed++;
        }

        return trimmed;
    }
}
