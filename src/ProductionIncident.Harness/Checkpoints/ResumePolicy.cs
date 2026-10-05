using ProductionIncident.Core.State;

namespace ProductionIncident.Harness.Checkpoints;

/// <summary>
/// Checkpoint/resume: the workflow saves a snapshot after every phase transition.
/// On restart, investigations in a resumable phase are re-queued and continue from that phase.
/// </summary>
public static class ResumePolicy
{
    public static bool ShouldResumeOnStartup(InvestigationPhase phase) =>
        !phase.IsTerminal() && phase != InvestigationPhase.AwaitingApproval;
}
