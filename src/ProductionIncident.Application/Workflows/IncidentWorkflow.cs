using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionIncident.Agents;
using ProductionIncident.Agents.Remediation;
using ProductionIncident.Agents.RootCause;
using ProductionIncident.Agents.Runtime;
using ProductionIncident.Agents.Specialists;
using ProductionIncident.Agents.Supervisor;
using ProductionIncident.Agents.Triage;
using ProductionIncident.Application.Incidents;
using ProductionIncident.Application.Knowledge;
using ProductionIncident.Application.Orchestration;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.State;
using ProductionIncident.Core.Tools;
using ProductionIncident.Harness;
using ProductionIncident.Harness.Budgets;
using ProductionIncident.Harness.Checkpoints;
using ProductionIncident.Harness.Context;
using ProductionIncident.Harness.Planning;
using ProductionIncident.LlmOps.Receipts;
using ProductionIncident.LlmOps.Tracing;
using ProductionIncident.Memory.Consolidation;
using ProductionIncident.Memory.RetrievalGate;

namespace ProductionIncident.Application.Workflows;

/// <summary>
/// The deterministic end-to-end process around agentic decisions (blueprint §10, §16):
/// Triage → Concurrent investigation → Root cause → Confidence router → (Deep-dive handoff ↺) →
/// Remediation → Human approval → Action → Verification → Close &amp; learn.
/// State is checkpointed after every phase, so an interrupted investigation resumes where it stopped.
/// </summary>
public sealed class IncidentWorkflow
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly Dictionary<string, ISpecialistAgent> _specialists;
    private readonly IInvestigationStore _store;
    private readonly IInvestigationQueue _queue;
    private readonly IInvestigationControl _control;
    private readonly TriageAgent _triage;
    private readonly SupervisorAgent _supervisor;
    private readonly RootCauseAgent _rootCause;
    private readonly RemediationAgent _remediation;
    private readonly KnowledgeToolFactory _knowledgeTools;
    private readonly IApprovalService _approvals;
    private readonly ActionExecutor _executor;
    private readonly VerificationService _verification;
    private readonly MemoryConsolidator _consolidator;
    private readonly IKnowledgeBase _knowledgeBase;
    private readonly ITurnReceiptStore _receipts;
    private readonly IBudgetTracker _budget;
    private readonly HarnessOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<IncidentWorkflow> _logger;

    public IncidentWorkflow(
        IInvestigationStore store,
        IInvestigationQueue queue,
        IInvestigationControl control,
        TriageAgent triage,
        SupervisorAgent supervisor,
        RootCauseAgent rootCause,
        RemediationAgent remediation,
        IEnumerable<ISpecialistAgent> specialists,
        KnowledgeToolFactory knowledgeTools,
        IApprovalService approvals,
        ActionExecutor executor,
        VerificationService verification,
        MemoryConsolidator consolidator,
        IKnowledgeBase knowledgeBase,
        ITurnReceiptStore receipts,
        IBudgetTracker budget,
        IOptions<HarnessOptions> options,
        TimeProvider time,
        ILogger<IncidentWorkflow> logger)
    {
        _store = store;
        _queue = queue;
        _control = control;
        _triage = triage;
        _supervisor = supervisor;
        _rootCause = rootCause;
        _remediation = remediation;
        _specialists = specialists.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        _knowledgeTools = knowledgeTools;
        _approvals = approvals;
        _executor = executor;
        _verification = verification;
        _consolidator = consolidator;
        _knowledgeBase = knowledgeBase;
        _receipts = receipts;
        _budget = budget;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────── Phase A — ingestion

    public async Task<InvestigationState> StartAsync(IncidentRequest request, CancellationToken cancellationToken)
    {
        var id = string.IsNullOrWhiteSpace(request.IncidentId)
            ? $"INC-{_time.GetUtcNow():yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}"
            : request.IncidentId.Trim();

        // Idempotent ingestion: the same alert delivered twice does not start two investigations.
        if (await _store.GetAsync(id, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return existing;
        }

        var state = new InvestigationState
        {
            IncidentId = id,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Service = string.IsNullOrWhiteSpace(request.Service) ? null : request.Service.Trim().ToLowerInvariant(),
            Severity = request.Severity,
            Source = request.Source,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
        };
        state.Log($"Incident received from {request.Source}.");

        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        await _queue.EnqueueAsync(id, cancellationToken).ConfigureAwait(false);
        return state;
    }

    /// <summary>Runs (or resumes) an investigation until it is terminal or waiting for human approval.</summary>
    public async Task<InvestigationState?> RunAsync(string incidentId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(incidentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await _store.GetAsync(incidentId, cancellationToken).ConfigureAwait(false);
            if (state is null)
            {
                return null;
            }

            using var activity = IncidentTelemetry.ActivitySource.StartActivity("incident.workflow");
            activity?.SetTag(IncidentTelemetry.Tags.IncidentId, incidentId);

            while (!state.Phase.IsTerminal() && state.Phase != InvestigationPhase.AwaitingApproval)
            {
                if (_control.IsStopRequested(incidentId, out var reason))
                {
                    state.StopReason = $"Stopped by operator: {reason}";
                    Transition(state, InvestigationPhase.Stopped);
                    break;
                }

                var phase = state.Phase;
                try
                {
                    using var phaseActivity = IncidentTelemetry.ActivitySource.StartActivity($"workflow.{phase}");
                    phaseActivity?.SetTag(IncidentTelemetry.Tags.Phase, phase.ToString());
                    phaseActivity?.SetTag(IncidentTelemetry.Tags.Round, state.Round);
                    await StepAsync(state, cancellationToken).ConfigureAwait(false);
                }
                catch (BudgetExceededException ex)
                {
                    state.StopReason = ex.Message;
                    Transition(state, InvestigationPhase.Escalated);
                }
                catch (InvestigationStoppedException ex)
                {
                    state.StopReason = ex.Message;
                    Transition(state, InvestigationPhase.Stopped);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Workflow step {Phase} failed for {IncidentId}", phase, incidentId);
                    state.StopReason = $"{phase} failed: {ex.Message}";
                    Transition(state, InvestigationPhase.Failed);
                }

                // Checkpoint after every phase.
                state.UpdatedAt = _time.GetUtcNow();
                await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            }

            if (state.Phase.IsTerminal() && state.FinalReport is null)
            {
                FinalizeInvestigation(state);
                await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            }

            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    private Task StepAsync(InvestigationState state, CancellationToken ct) => state.Phase switch
    {
        InvestigationPhase.Triage => TriageAsync(state, ct),
        InvestigationPhase.ConcurrentInvestigation => ConcurrentInvestigationAsync(state, ct),
        InvestigationPhase.RootCause => RootCauseAsync(state, ct),
        InvestigationPhase.DeepDive => DeepDiveAsync(state, ct),
        InvestigationPhase.Remediation => RemediationAsync(state, ct),
        InvestigationPhase.ExecutingActions => ExecuteActionsAsync(state, ct),
        InvestigationPhase.Verification => VerifyAsync(state, ct),
        InvestigationPhase.Closing => CloseAsync(state, ct),
        _ => throw new InvalidOperationException($"No step for phase {state.Phase}."),
    };

    // ─────────────────────────────────────────────────────────────── Phase B — triage

    private async Task TriageAsync(InvestigationState state, CancellationToken ct)
    {
        var (triage, run) = await _triage.TriageAsync(state.IncidentId, Context(state), state.Service, ct).ConfigureAwait(false);
        EnsureUsable(run.Status, run.Error);

        state.Triage = triage;
        state.Service ??= triage.AffectedServices.FirstOrDefault();
        if (state.Service is null)
        {
            state.StopReason = "Triage could not identify an affected service.";
            Transition(state, InvestigationPhase.Escalated);
            return;
        }

        InvestigationPlanner.CreatePlan(state, triage.AgentsToRun);
        state.Log($"Triage: {triage.Category}/{triage.Severity}, services [{string.Join(", ", triage.AffectedServices)}], strategy {triage.Strategy}, agents [{string.Join(", ", triage.AgentsToRun)}]. {triage.Summary}");
        Transition(state, InvestigationPhase.ConcurrentInvestigation);
    }

    // ─────────────────────────────────────────────────────────────── Phase C/D — concurrent round + aggregation

    private async Task ConcurrentInvestigationAsync(InvestigationState state, CancellationToken ct)
    {
        state.Round = 1;
        var agents = (state.Triage?.AgentsToRun ?? AgentNames.Specialists).Where(_specialists.ContainsKey).ToList();
        agents.ForEach(a => InvestigationPlanner.Start(state, InvestigationPlanner.Ids.Specialist(a)));

        var context = Context(state);
        var results = await Task.WhenAll(agents.Select(a =>
            _specialists[a].InvestigateAsync(new SpecialistRequest(state.IncidentId, context, AgentModes.Broad, null, []), ct))).ConfigureAwait(false);

        foreach (var result in results)
        {
            EnsureUsable(result.Run.Status, result.Run.Error);
        }

        var aggregation = EvidenceAggregator.Merge(state, results.Select(r => r.Evidence));
        state.LastRoundNewEvidence = aggregation.NewEvidence;
        agents.ForEach(a => InvestigationPlanner.Complete(state, InvestigationPlanner.Ids.Specialist(a)));

        state.Log($"Round 1 (concurrent: {string.Join(", ", agents)}): {aggregation.NewEvidence} evidence item(s), {state.OpenQuestions.Count} open question(s), {aggregation.Conflicts.Count} conflict(s).");
        foreach (var conflict in aggregation.Conflicts)
        {
            state.Log($"Conflict detected — {conflict}");
        }

        Transition(state, InvestigationPhase.RootCause);
    }

    // ─────────────────────────────────────────────────────────────── Phase E/F — root cause + confidence routing

    private async Task RootCauseAsync(InvestigationState state, CancellationToken ct)
    {
        InvestigationPlanner.Start(state, InvestigationPlanner.Ids.RootCause);

        var gate = MemoryRetrievalGate.ShouldRetrieve(AgentNames.RootCause, state);
        var retrieved = new List<string>();
        var tools = new List<ToolDescriptor> { _knowledgeTools.SearchKnowledge() };
        if (gate.Retrieve)
        {
            tools.Add(_knowledgeTools.RecallSimilarIncidents(retrieved));
        }

        state.Log($"Retrieval gate: {(gate.Retrieve ? "retrieve" : "skip")} long-term memory — {gate.Reason}");

        var (analysis, run) = await _rootCause.AnalyzeAsync(state.IncidentId, Context(state), tools, retrieved, ct).ConfigureAwait(false);
        EnsureUsable(run.Status, run.Error);

        foreach (var memoryId in retrieved.Distinct().Where(m => !state.MemoryRetrieved.Contains(m)))
        {
            state.MemoryRetrieved.Add(memoryId);
        }

        state.RootCause = analysis;
        state.RootCauseHistory.Add(analysis);
        state.CurrentRootCause = analysis.Cause;
        state.RootCauseConfidence = analysis.Confidence;

        var route = ConfidenceRouter.Route(analysis, state.Round, state.LastRoundNewEvidence, _options);
        state.Log($"Root cause (round {state.Round}): \"{analysis.Cause}\" — confidence {analysis.Confidence:0.00}, {analysis.Contradictions.Count} contradiction(s), {analysis.MissingEvidence.Count} missing evidence. Route: {route.Kind} — {route.Reason}");

        switch (route.Kind)
        {
            case RouteKind.Remediate:
                InvestigationPlanner.Complete(state, InvestigationPlanner.Ids.RootCause);
                Transition(state, InvestigationPhase.Remediation);
                break;
            case RouteKind.DeepDive:
                Transition(state, InvestigationPhase.DeepDive);
                break;
            default:
                state.StopReason = route.Reason;
                Transition(state, InvestigationPhase.Escalated);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────── Phase G — supervisor + handoff deep dive

    private async Task DeepDiveAsync(InvestigationState state, CancellationToken ct)
    {
        state.Round++;
        var (decision, run) = await _supervisor.DecideAsync(state.IncidentId, Context(state), ct).ConfigureAwait(false);
        EnsureUsable(run.Status, run.Error);

        var agents = decision.AgentsToRun.Where(_specialists.ContainsKey).ToList();
        if (agents.Count == 0 && !decision.InvestigationComplete)
        {
            agents = SupervisorFallback.FromMissingEvidence(state.RootCause?.MissingEvidence ?? []).Where(_specialists.ContainsKey).ToList();
        }

        if (decision.InvestigationComplete || agents.Count == 0)
        {
            state.StopReason = $"Supervisor escalated to a human: {decision.Reason}";
            Transition(state, InvestigationPhase.Escalated);
            return;
        }

        state.Log($"Supervisor (round {state.Round}): start with {agents[0]}{(agents.Count > 1 ? $", then {string.Join(", ", agents.Skip(1))}" : "")} — {decision.Reason}");

        var baseFocus = $"{decision.Reason} Missing evidence: {string.Join("; ", state.RootCause?.MissingEvidence ?? [])}".Trim();
        var focus = baseFocus;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newEvidence = 0;
        var handoffs = 0;
        string? current = agents[0];

        // Handoff chain: each specialist follows the strongest clue and may transfer control along allowed routes.
        while (current is not null && visited.Add(current))
        {
            IReadOnlyList<string> targets = handoffs < _options.MaxHandoffsPerRound
                ? HandoffTopology.AllowedTargets(current).Where(t => !visited.Contains(t) && _specialists.ContainsKey(t)).ToList()
                : [];

            var result = await _specialists[current]
                .InvestigateAsync(new SpecialistRequest(state.IncidentId, Context(state), AgentModes.DeepDive, focus, targets), ct)
                .ConfigureAwait(false);
            EnsureUsable(result.Run.Status, result.Run.Error);

            newEvidence += EvidenceAggregator.Merge(state, [result.Evidence]).NewEvidence;

            if (result.Handoff is { } handoff &&
                targets.Contains(handoff.Target, StringComparer.OrdinalIgnoreCase) &&
                HandoffTopology.IsAllowed(current, handoff.Target))
            {
                state.Handoffs.Add(new HandoffRecord(state.Round, current, handoff.Target, handoff.Reason));
                state.Log($"Handoff {current} → {handoff.Target}: {handoff.Reason}");
                handoffs++;
                focus = handoff.Reason;
                current = handoff.Target;
            }
            else
            {
                current = null;
            }
        }

        // Other specialists requested by the supervisor and not reached by the chain run concurrently.
        var remaining = agents.Skip(1).Where(a => !visited.Contains(a)).ToList();
        if (remaining.Count > 0)
        {
            var context = Context(state);
            var results = await Task.WhenAll(remaining.Select(a =>
                _specialists[a].InvestigateAsync(new SpecialistRequest(state.IncidentId, context, AgentModes.DeepDive, baseFocus, []), ct))).ConfigureAwait(false);
            foreach (var r in results)
            {
                EnsureUsable(r.Run.Status, r.Run.Error);
            }

            newEvidence += EvidenceAggregator.Merge(state, results.Select(r => r.Evidence)).NewEvidence;
        }

        state.LastRoundNewEvidence = newEvidence;
        state.Log($"Round {state.Round} (deep dive: {string.Join(" → ", visited)}{(remaining.Count > 0 ? $" + {string.Join(", ", remaining)}" : "")}): {newEvidence} new evidence item(s), {state.Conflicts.Count} conflict(s) remaining.");
        Transition(state, InvestigationPhase.RootCause);
    }

    // ─────────────────────────────────────────────────────────────── Phase H — remediation + approval

    private async Task RemediationAsync(InvestigationState state, CancellationToken ct)
    {
        InvestigationPlanner.Start(state, InvestigationPlanner.Ids.Remediation);

        var tools = new List<ToolDescriptor> { _knowledgeTools.SearchKnowledge() };
        var gate = MemoryRetrievalGate.ShouldRetrieve(AgentNames.Remediation, state);
        var retrieved = new List<string>();
        if (gate.Retrieve)
        {
            tools.Add(_knowledgeTools.RecallSimilarIncidents(retrieved));
        }

        var (plan, run) = await _remediation.ProposeAsync(state.IncidentId, Context(state), tools, ct).ConfigureAwait(false);
        EnsureUsable(run.Status, run.Error);

        IReadOnlyList<string> affected = state.Triage?.AffectedServices is { Count: > 0 } services
            ? services
            : state.Service is null ? [] : [state.Service];
        var validated = RemediationValidator.Validate(plan, affected);
        state.Remediation = plan with { ImmediateMitigation = validated.Actions };
        foreach (var rejected in validated.Rejected)
        {
            state.Log($"Proposed action rejected by policy: {rejected}");
        }

        if (run.BlockedToolCalls.Count > 0)
        {
            state.Log($"Harness blocked {run.BlockedToolCalls.Count} production-changing tool call(s) attempted by {run.Agent}: {string.Join(", ", run.BlockedToolCalls)}");
        }

        InvestigationPlanner.Complete(state, InvestigationPlanner.Ids.Remediation);

        if (validated.Actions.Count == 0)
        {
            state.Log("No production-changing mitigation proposed; verifying current state.");
            InvestigationPlanner.Skip(state, InvestigationPlanner.Ids.Approval);
            Transition(state, InvestigationPhase.Verification);
            return;
        }

        foreach (var action in validated.Actions)
        {
            var request = await _approvals.RequestAsync(state.IncidentId, action, ct).ConfigureAwait(false);
            state.ApprovalIds.Add(request.Id);
            state.Log($"Approval requested {request.Id}: {action.Tool} ({action.Risk} risk) — {action.Rationale}");
        }

        InvestigationPlanner.Start(state, InvestigationPlanner.Ids.Approval);
        Transition(state, InvestigationPhase.AwaitingApproval);
    }

    /// <summary>Called after a human decided on an approval request. Resumes the workflow once all are decided.</summary>
    public async Task<InvestigationState?> OnApprovalDecidedAsync(string incidentId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(incidentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await _store.GetAsync(incidentId, cancellationToken).ConfigureAwait(false);
            if (state is null || state.Phase != InvestigationPhase.AwaitingApproval)
            {
                return state;
            }

            var requests = (await _approvals.ListAsync(incidentId, null, cancellationToken).ConfigureAwait(false))
                .Where(r => state.ApprovalIds.Contains(r.Id))
                .ToList();

            if (requests.Any(r => r.Status == ApprovalStatus.Pending))
            {
                return state;
            }

            foreach (var r in requests)
            {
                state.Log($"Approval {r.Id} {r.Status.ToString().ToLowerInvariant()} by {r.DecidedBy}{(string.IsNullOrWhiteSpace(r.Comment) ? "" : $": {r.Comment}")}");
            }

            InvestigationPlanner.Complete(state, InvestigationPlanner.Ids.Approval);
            if (requests.All(r => r.Status == ApprovalStatus.Rejected))
            {
                state.StopReason = "All proposed production actions were rejected by the operator.";
                Transition(state, InvestigationPhase.Escalated);
                FinalizeInvestigation(state);
                await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
                return state;
            }

            Transition(state, InvestigationPhase.ExecutingActions);
            state.UpdatedAt = _time.GetUtcNow();
            await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            await _queue.EnqueueAsync(incidentId, cancellationToken).ConfigureAwait(false);
            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ExecuteActionsAsync(InvestigationState state, CancellationToken ct)
    {
        var executed = await _executor.ExecuteApprovedAsync(state.IncidentId, ct).ConfigureAwait(false);
        foreach (var line in executed)
        {
            state.ExecutedActions.Add(line);
            state.Log($"Action: {line}");
        }

        Transition(state, InvestigationPhase.Verification);
    }

    private async Task VerifyAsync(InvestigationState state, CancellationToken ct)
    {
        InvestigationPlanner.Start(state, InvestigationPlanner.Ids.Verify);
        var service = state.Service ?? state.Triage?.AffectedServices.FirstOrDefault() ?? "unknown";
        var result = await _verification.VerifyAsync(service, ct).ConfigureAwait(false);

        state.Verified = result.Recovered;
        state.VerificationSummary = result.Summary;
        state.Log($"Verification: {result.Summary}");

        if (result.Recovered)
        {
            InvestigationPlanner.Complete(state, InvestigationPlanner.Ids.Verify);
            Transition(state, InvestigationPhase.Closing);
        }
        else
        {
            state.StopReason = "Recovery not verified: human intervention required.";
            Transition(state, InvestigationPhase.Escalated);
        }
    }

    // ─────────────────────────────────────────────────────────────── Phase I — close and learn

    private async Task CloseAsync(InvestigationState state, CancellationToken ct)
    {
        InvestigationPlanner.Start(state, InvestigationPlanner.Ids.Learn);

        var consolidation = await _consolidator.ConsolidateAsync(state, ct).ConfigureAwait(false);
        state.MemoryStored.AddRange(consolidation.Stored);
        foreach (var stored in consolidation.Stored)
        {
            state.Log($"Memory admitted: {stored}");
        }

        foreach (var rejected in consolidation.Rejected)
        {
            state.Log($"Memory not admitted: {rejected}");
        }

        InvestigationPlanner.Complete(state, InvestigationPlanner.Ids.Learn);
        Transition(state, InvestigationPhase.Resolved);
        FinalizeInvestigation(state);

        if (state.Verified == true && state.FinalReport is not null)
        {
            await _knowledgeBase.IndexAsync(
                new KnowledgeDocument($"postmortem:{state.IncidentId}", $"Postmortem {state.IncidentId} — {state.Title}", "postmortem", state.FinalReport, [state.Service ?? "", "postmortem"]),
                ct).ConfigureAwait(false);
            state.Log("Postmortem draft indexed into the knowledge base (RAG).");
            state.FinalReport = IncidentReportBuilder.Build(state);
        }
    }

    /// <summary>Online evals + workflow receipt + final report, for every terminal state.</summary>
    private void FinalizeInvestigation(InvestigationState state)
    {
        var receipts = _receipts.List(state.IncidentId);
        state.OnlineEvalScores.Clear();
        foreach (var (key, value) in OnlineEvaluator.Score(state, receipts))
        {
            state.OnlineEvalScores[key] = value;
        }

        var budget = _budget.Get(state.IncidentId);
        _receipts.Add(new TurnReceipt(
            RunId: $"wf-{state.IncidentId}",
            IncidentId: state.IncidentId,
            Agent: "IncidentWorkflow",
            Model: "n/a",
            PromptVersion: "n/a",
            SkillVersion: "n/a",
            ToolCalls: [],
            BlockedToolCalls: [],
            Handoff: null,
            Iterations: state.Round,
            TokensIn: budget.TokensIn,
            TokensOut: budget.TokensOut,
            CostUsd: budget.CostUsd,
            LatencyMs: (state.UpdatedAt - state.CreatedAt).TotalMilliseconds,
            MemoryRetrieved: state.MemoryRetrieved.ToList(),
            MemoryStored: state.MemoryStored.ToList(),
            EvalScores: new Dictionary<string, double>(state.OnlineEvalScores),
            FinalStatus: state.Phase.ToString(),
            StartedAt: state.CreatedAt));

        state.FinalReport = IncidentReportBuilder.Build(state);
    }

    // ─────────────────────────────────────────────────────────────── operator controls

    /// <summary>Operator kill switch.</summary>
    public async Task<InvestigationState?> StopAsync(string incidentId, string reason, CancellationToken cancellationToken)
    {
        _control.RequestStop(incidentId, reason);

        // If nothing is running (e.g. waiting for approval), stop it right away.
        var gate = _locks.GetOrAdd(incidentId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return await _store.GetAsync(incidentId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var state = await _store.GetAsync(incidentId, cancellationToken).ConfigureAwait(false);
            if (state is not null && !state.Phase.IsTerminal())
            {
                state.StopReason = $"Stopped by operator: {reason}";
                Transition(state, InvestigationPhase.Stopped);
                FinalizeInvestigation(state);
                state.UpdatedAt = _time.GetUtcNow();
                await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            }

            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Checkpoint/resume: re-queue investigations interrupted by a restart.</summary>
    public async Task<int> ResumeInterruptedAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var summary in await _store.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (ResumePolicy.ShouldResumeOnStartup(summary.Phase))
            {
                await _queue.EnqueueAsync(summary.IncidentId, cancellationToken).ConfigureAwait(false);
                count++;
            }
        }

        return count;
    }

    // ─────────────────────────────────────────────────────────────── helpers

    private string Context(InvestigationState state) => ContextCompactor.ToPrompt(state, _options.MaxFactsInContext);

    private static void Transition(InvestigationState state, InvestigationPhase next)
    {
        var previous = state.Phase;
        state.Phase = next;
        state.Log($"{previous} → {next}");
    }

    private static void EnsureUsable(AgentRunStatus status, string? error)
    {
        switch (status)
        {
            case AgentRunStatus.BudgetExceeded:
                throw new BudgetExceededException(error ?? "Budget exceeded.");
            case AgentRunStatus.Stopped:
                throw new InvestigationStoppedException(error ?? "Stopped.");
        }
    }
}

/// <summary>Deterministic fallback when the Supervisor's output is unusable: map missing evidence to specialists.</summary>
public static class SupervisorFallback
{
    public static IReadOnlyList<string> FromMissingEvidence(IEnumerable<string> missingEvidence)
    {
        var agents = new List<string>();
        foreach (var item in missingEvidence.Select(m => m.ToLowerInvariant()))
        {
            string? agent = item switch
            {
                _ when item.Contains("diff") || item.Contains("deploy") || item.Contains("release") || item.Contains("config") => AgentNames.Deployment,
                _ when item.Contains("pool") || item.Contains("database") || item.Contains("query") || item.Contains("lock") => AgentNames.Database,
                _ when item.Contains("cpu") || item.Contains("memory") || item.Contains("latency") || item.Contains("metric") || item.Contains("rate") => AgentNames.Metrics,
                _ when item.Contains("log") || item.Contains("trace") || item.Contains("exception") => AgentNames.Logs,
                _ => null,
            };

            if (agent is not null && !agents.Contains(agent))
            {
                agents.Add(agent);
            }
        }

        return agents;
    }
}
