using System.Text;
using ProductionIncident.Core.State;

namespace ProductionIncident.Application.Workflows;

/// <summary>Final incident report / postmortem draft (deterministic rendering of the investigation state).</summary>
public static class IncidentReportBuilder
{
    public static string Build(InvestigationState s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {s.IncidentId} — {s.Title}");
        sb.AppendLine();
        sb.AppendLine($"- **Service:** {s.Service ?? string.Join(", ", s.Triage?.AffectedServices ?? [])}");
        sb.AppendLine($"- **Severity:** {s.Triage?.Severity ?? s.Severity ?? "n/a"} · **Category:** {s.Triage?.Category ?? "n/a"}");
        sb.AppendLine($"- **Status:** {s.Phase}{(s.StopReason is null ? "" : $" ({s.StopReason})")}");
        sb.AppendLine($"- **Investigation rounds:** {s.Round} · **Handoffs:** {s.Handoffs.Count}");
        sb.AppendLine();

        sb.AppendLine("## Root cause");
        sb.AppendLine($"{s.CurrentRootCause ?? "Undetermined"} (confidence {s.RootCauseConfidence:0.00})");
        sb.AppendLine();
        if (s.RootCause is { } rca)
        {
            sb.AppendLine("### Supporting evidence");
            foreach (var e in rca.SupportingEvidence)
            {
                sb.AppendLine($"- {e}");
            }

            if (rca.Contradictions.Count > 0)
            {
                sb.AppendLine("### Unresolved contradictions");
                foreach (var c in rca.Contradictions)
                {
                    sb.AppendLine($"- {c}");
                }
            }

            if (rca.MissingEvidence.Count > 0)
            {
                sb.AppendLine("### Missing evidence");
                foreach (var m in rca.MissingEvidence)
                {
                    sb.AppendLine($"- {m}");
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Evidence by agent");
        foreach (var group in s.AgentResults.GroupBy(r => r.AgentName))
        {
            sb.AppendLine($"### {group.Key}");
            foreach (var e in group.SelectMany(r => r.Evidence))
            {
                sb.AppendLine($"- ({e.Type}) {e.Description} — _{e.Source}_");
            }
        }

        sb.AppendLine();
        if (s.Handoffs.Count > 0)
        {
            sb.AppendLine("## Deep-dive handoffs");
            foreach (var h in s.Handoffs)
            {
                sb.AppendLine($"- Round {h.Round}: {h.From} → {h.To}: {h.Reason}");
            }

            sb.AppendLine();
        }

        if (s.Remediation is { } plan)
        {
            sb.AppendLine("## Remediation");
            sb.AppendLine("### Immediate mitigation");
            foreach (var a in plan.ImmediateMitigation)
            {
                sb.AppendLine($"- `{a.Tool}` ({a.Risk} risk): {a.Rationale}");
            }

            sb.AppendLine("### Permanent fix");
            foreach (var f in plan.PermanentFix)
            {
                sb.AppendLine($"- {f}");
            }

            sb.AppendLine("### Verification plan");
            foreach (var v in plan.VerificationPlan)
            {
                sb.AppendLine($"- {v}");
            }

            sb.AppendLine("### Rollback plan");
            foreach (var r in plan.RollbackPlan)
            {
                sb.AppendLine($"- {r}");
            }

            sb.AppendLine();
        }

        if (s.ExecutedActions.Count > 0)
        {
            sb.AppendLine("## Actions executed (after human approval)");
            foreach (var a in s.ExecutedActions)
            {
                sb.AppendLine($"- {a}");
            }

            sb.AppendLine();
        }

        if (s.VerificationSummary is not null)
        {
            sb.AppendLine("## Verification");
            sb.AppendLine(s.VerificationSummary);
            sb.AppendLine();
        }

        sb.AppendLine("## Timeline");
        foreach (var t in s.Timeline)
        {
            sb.AppendLine($"- {t.At:yyyy-MM-dd HH:mm:ss}Z [{t.Phase}] {t.Message}");
        }

        return sb.ToString();
    }
}
