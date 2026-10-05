using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ProductionIncident.LlmOps.Tracing;

/// <summary>OpenTelemetry-compatible traces and metrics (blueprint §19). Exported by the host.</summary>
public static class IncidentTelemetry
{
    public const string SourceName = "ProductionIncident";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> TokensIn = Meter.CreateCounter<long>("incident.llm.tokens.input");
    public static readonly Counter<long> TokensOut = Meter.CreateCounter<long>("incident.llm.tokens.output");
    public static readonly Counter<double> CostUsd = Meter.CreateCounter<double>("incident.llm.cost.usd");
    public static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>("incident.tool.calls");
    public static readonly Counter<long> BlockedActions = Meter.CreateCounter<long>("incident.tool.blocked");
    public static readonly Counter<long> Handoffs = Meter.CreateCounter<long>("incident.agent.handoffs");
    public static readonly Histogram<double> AgentLatencyMs = Meter.CreateHistogram<double>("incident.agent.latency", unit: "ms");
    public static readonly Histogram<double> ToolLatencyMs = Meter.CreateHistogram<double>("incident.tool.latency", unit: "ms");

    public static class Tags
    {
        public const string IncidentId = "incident.id";
        public const string RunId = "agent.run_id";
        public const string Agent = "agent.name";
        public const string Model = "gen_ai.request.model";
        public const string Tool = "tool.name";
        public const string Phase = "workflow.phase";
        public const string Round = "workflow.round";
        public const string Confidence = "rootcause.confidence";
        public const string Status = "status";
    }
}
