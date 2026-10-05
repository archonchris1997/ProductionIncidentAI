using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using ProductionIncident.Agents.Runtime;

namespace ProductionIncident.Infrastructure.Simulation;

/// <summary>
/// What the scripted model "sees" in a conversation: which agent it plays, the mode/focus, the compact incident
/// context, the tools offered, the tools already called and the observations made so far.
/// Observations are written by the scripted model itself into its assistant messages ("Observation from x: ...")
/// so they survive the harness's tool-result trimming — exactly what a real model's notes do.
/// </summary>
internal sealed partial class ScriptedConversation
{
    [GeneratedRegex("^# Agent: (\\S+)", RegexOptions.Multiline)]
    private static partial Regex AgentRegex();

    [GeneratedRegex("^# Mode: (\\S+)", RegexOptions.Multiline)]
    private static partial Regex ModeRegex();

    [GeneratedRegex("^# Focus: (.+)$", RegexOptions.Multiline)]
    private static partial Regex FocusRegex();

    [GeneratedRegex("^Observation from (\\w+): (.*)$", RegexOptions.Multiline)]
    private static partial Regex ObservationRegex();

    public string Agent { get; private init; } = "";
    public string Mode { get; private init; } = "";
    public string? Focus { get; private init; }
    public JsonElement? Context { get; private init; }
    public HashSet<string> Tools { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Called { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Tool, string Summary)> Observations { get; } = [];
    public List<(string Tool, string Summary)> NewObservations { get; } = [];

    public static ScriptedConversation Parse(IReadOnlyList<ChatMessage> messages, ChatOptions? options)
    {
        var system = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? "";
        var user = messages.FirstOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";

        JsonElement? context = null;
        var open = user.IndexOf(PromptBuilder.ContextOpen, StringComparison.Ordinal);
        var close = user.IndexOf(PromptBuilder.ContextClose, StringComparison.Ordinal);
        if (open >= 0 && close > open)
        {
            var json = user[(open + PromptBuilder.ContextOpen.Length)..close];
            try
            {
                context = JsonDocument.Parse(json).RootElement.Clone();
            }
            catch (JsonException)
            {
                context = null;
            }
        }

        var conversation = new ScriptedConversation
        {
            Agent = AgentRegex().Match(system) is { Success: true } a ? a.Groups[1].Value : "",
            Mode = ModeRegex().Match(user) is { Success: true } m ? m.Groups[1].Value : "",
            Focus = FocusRegex().Match(user) is { Success: true } f ? f.Groups[1].Value.Trim() : null,
            Context = context,
        };

        foreach (var tool in options?.Tools ?? [])
        {
            conversation.Tools.Add(tool.Name);
        }

        var callNames = new Dictionary<string, string>();
        var lastAssistant = -1;
        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].Role == ChatRole.Assistant)
            {
                lastAssistant = i;
            }
        }

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (message.Role == ChatRole.Assistant)
            {
                foreach (var call in message.Contents.OfType<FunctionCallContent>())
                {
                    callNames[call.CallId] = call.Name;
                    conversation.Called.Add(call.Name);
                }

                foreach (Match match in ObservationRegex().Matches(message.Text ?? ""))
                {
                    conversation.Observations.Add((match.Groups[1].Value, match.Groups[2].Value));
                }
            }
            else if (message.Role == ChatRole.Tool && i > lastAssistant)
            {
                foreach (var result in message.Contents.OfType<FunctionResultContent>())
                {
                    var name = callNames.GetValueOrDefault(result.CallId, "unknown");
                    var observation = (name, Summarize(result.Result?.ToString() ?? ""));
                    conversation.NewObservations.Add(observation);
                    conversation.Observations.Add(observation);
                }
            }
        }

        return conversation;
    }

    // ── helpers used by the scripts ─────────────────────────────────────

    public string? Obs(string tool) =>
        Observations.LastOrDefault(o => string.Equals(o.Tool, tool, StringComparison.OrdinalIgnoreCase)).Summary;

    public string AllObservations => string.Join("\n", Observations.Select(o => o.Summary));

    public string ContextString(string property) =>
        Context is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";

    public double ContextNumber(string property) =>
        Context is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetDouble()
            : 0;

    public IReadOnlyList<string> ContextList(string property) =>
        Context is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? "").ToList()
            : [];

    /// <summary>Everything the agent knows from the shared state (facts, hypotheses, conflicts) + its own observations.</summary>
    public string Corpus =>
        string.Join("\n", ContextList("establishedFacts")
            .Concat(ContextList("hypotheses"))
            .Concat(ContextList("conflicts"))
            .Append(ContextString("currentHypothesis"))
            .Append(AllObservations));

    private static string Summarize(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("Error", StringComparison.Ordinal) || trimmed.StartsWith("BLOCKED", StringComparison.Ordinal) || trimmed.StartsWith("Handoff", StringComparison.Ordinal))
        {
            return OneLine(trimmed);
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGet(root, "summary", out var summary) && summary.ValueKind == JsonValueKind.String)
                {
                    return OneLine(summary.GetString() ?? "");
                }

                if (TryGet(root, "incidents", out var incidents) && incidents.ValueKind == JsonValueKind.Array)
                {
                    var lines = incidents.EnumerateArray().Select(i =>
                        $"Similar past incident {Str(i, "incidentId")} ({Str(i, "service")}): {Str(i, "rootCause")} — resolved by {Str(i, "resolution")}").ToList();
                    return lines.Count == 0 ? "No similar past incident." : OneLine(string.Join(" | ", lines));
                }
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                var hits = root.EnumerateArray().Select(h => $"{Str(h, "title")} [{Str(h, "sourceId")}]").ToList();
                return hits.Count == 0 ? "No knowledge found." : "Knowledge: " + OneLine(string.Join(" | ", hits));
            }
        }
        catch (JsonException)
        {
            // not JSON: fall through
        }

        return OneLine(trimmed.Length > 300 ? trimmed[..300] : trimmed);
    }

    private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

    private static string Str(JsonElement e, string name) =>
        TryGet(e, name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText()) : "";

    private static bool TryGet(JsonElement e, string name, out JsonElement value)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
