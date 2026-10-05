using ProductionIncident.Core.Contracts;
using ProductionIncident.Core.State;

namespace ProductionIncident.Harness.Planning;

/// <summary>Planning + todo tracking (blueprint §13.1). Keeps long-running work resumable and readable.</summary>
public static class InvestigationPlanner
{
    public static class Ids
    {
        public const string Identify = "identify-service";
        public const string RootCause = "root-cause";
        public const string Remediation = "remediation";
        public const string Approval = "approval";
        public const string Verify = "verify";
        public const string Learn = "learn";

        public static string Specialist(string agent) => $"inspect-{agent}";
    }

    public static void CreatePlan(InvestigationState state, IReadOnlyList<string> specialists)
    {
        if (state.Todos.Count > 0)
        {
            return;
        }

        state.Todos.Add(new TodoItem { Id = Ids.Identify, Title = "Identify affected service", Status = TodoStatus.Done });
        foreach (var agent in specialists)
        {
            state.Todos.Add(new TodoItem { Id = Ids.Specialist(agent), Title = SpecialistTitle(agent) });
        }

        state.Todos.Add(new TodoItem { Id = Ids.RootCause, Title = "Determine root cause" });
        state.Todos.Add(new TodoItem { Id = Ids.Remediation, Title = "Propose remediation" });
        state.Todos.Add(new TodoItem { Id = Ids.Approval, Title = "Obtain human approval for production changes" });
        state.Todos.Add(new TodoItem { Id = Ids.Verify, Title = "Verify recovery" });
        state.Todos.Add(new TodoItem { Id = Ids.Learn, Title = "Close incident and consolidate learnings" });
    }

    public static void Start(InvestigationState state, string id) => Set(state, id, TodoStatus.InProgress);

    public static void Complete(InvestigationState state, string id) => Set(state, id, TodoStatus.Done);

    public static void Skip(InvestigationState state, string id) => Set(state, id, TodoStatus.Skipped);

    private static void Set(InvestigationState state, string id, TodoStatus status)
    {
        var item = state.Todos.FirstOrDefault(t => t.Id == id);
        if (item is not null)
        {
            item.Status = status;
        }
    }

    private static string SpecialistTitle(string agent) => agent switch
    {
        AgentNames.Logs => "Inspect logs and traces",
        AgentNames.Database => "Inspect database health and history",
        AgentNames.Metrics => "Inspect metrics",
        AgentNames.Deployment => "Compare latest deployment",
        _ => $"Run {agent}",
    };
}
