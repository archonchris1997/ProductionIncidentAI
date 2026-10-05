using ProductionIncident.Core.Contracts;

namespace ProductionIncident.Core.State;

/// <summary>
/// Canonical shared investigation state (working memory, blueprint §6.1).
/// Mutated only by the workflow (single writer); readers always get a snapshot from the store.
/// </summary>
public sealed class InvestigationState
{
    public required string IncidentId { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Service { get; set; }
    public string? Severity { get; init; }
    public string Source { get; init; } = "api";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public InvestigationPhase Phase { get; set; } = InvestigationPhase.Triage;
    public int Round { get; set; }
    public int LastRoundNewEvidence { get; set; }

    public TriageResult? Triage { get; set; }

    public List<AgentEvidence> AgentResults { get; init; } = [];
    public HashSet<string> CompletedAgents { get; init; } = [];
    public List<string> OpenQuestions { get; init; } = [];
    public List<string> Conflicts { get; init; } = [];

    public string? CurrentRootCause { get; set; }
    public double RootCauseConfidence { get; set; }
    public RootCauseAnalysis? RootCause { get; set; }
    public List<RootCauseAnalysis> RootCauseHistory { get; init; } = [];

    public List<HandoffRecord> Handoffs { get; init; } = [];
    public List<TodoItem> Todos { get; init; } = [];

    public RemediationPlan? Remediation { get; set; }
    public List<string> ApprovalIds { get; init; } = [];
    public List<string> ExecutedActions { get; init; } = [];
    public string? VerificationSummary { get; set; }
    public bool? Verified { get; set; }

    public Dictionary<string, double> OnlineEvalScores { get; init; } = [];
    public List<string> MemoryRetrieved { get; init; } = [];
    public List<string> MemoryStored { get; init; } = [];

    public List<TimelineEntry> Timeline { get; init; } = [];
    public string? StopReason { get; set; }
    public string? FinalReport { get; set; }

    public void Log(string message) =>
        Timeline.Add(new TimelineEntry(DateTimeOffset.UtcNow, Phase.ToString(), message));

    public IEnumerable<Evidence> AllEvidence() => AgentResults.SelectMany(r => r.Evidence);
}

public enum InvestigationPhase
{
    Triage,
    ConcurrentInvestigation,
    RootCause,
    DeepDive,
    Remediation,
    AwaitingApproval,
    ExecutingActions,
    Verification,
    Closing,
    Resolved,
    Escalated,
    Stopped,
    Failed,
}

public static class InvestigationPhaseExtensions
{
    public static bool IsTerminal(this InvestigationPhase phase) =>
        phase is InvestigationPhase.Resolved or InvestigationPhase.Escalated
            or InvestigationPhase.Stopped or InvestigationPhase.Failed;
}

public sealed record TimelineEntry(DateTimeOffset At, string Phase, string Message);

public sealed class TodoItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public TodoStatus Status { get; set; } = TodoStatus.Pending;
}

public enum TodoStatus
{
    Pending,
    InProgress,
    Done,
    Skipped,
}

public sealed record InvestigationSummary(
    string IncidentId,
    string Title,
    string? Service,
    InvestigationPhase Phase,
    int Round,
    string? RootCause,
    double Confidence,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static InvestigationSummary From(InvestigationState s) =>
        new(s.IncidentId, s.Title, s.Service, s.Phase, s.Round, s.CurrentRootCause, s.RootCauseConfidence, s.CreatedAt, s.UpdatedAt);
}
