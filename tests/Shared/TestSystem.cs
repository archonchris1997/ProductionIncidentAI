using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProductionIncident.Application.Incidents;
using ProductionIncident.Application.Workflows;
using ProductionIncident.Core.Abstractions;
using ProductionIncident.Core.State;
using ProductionIncident.Infrastructure;
using ProductionIncident.LlmOps.Receipts;
using ProductionIncident.Mcp.Backends.Fake;

namespace ProductionIncident.Tests.Shared;

/// <summary>
/// A fully wired incident system (scripted model + fake production estate) for tests and evals.
/// Each instance is isolated: its own fake environment, stores, memory and knowledge base.
/// </summary>
public sealed class TestSystem : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private TestSystem(ServiceProvider provider) => _provider = provider;

    public IServiceProvider Services => _provider;

    public IncidentWorkflow Workflow => _provider.GetRequiredService<IncidentWorkflow>();

    public IApprovalService Approvals => _provider.GetRequiredService<IApprovalService>();

    public IInvestigationStore Store => _provider.GetRequiredService<IInvestigationStore>();

    public ITurnReceiptStore Receipts => _provider.GetRequiredService<ITurnReceiptStore>();

    public FakeProductionBackend Production => _provider.GetRequiredService<FakeProductionBackend>();

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public static TestSystem Create(IDictionary<string, string?>? settings = null, IChatClient? chatClient = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["AI:Provider"] = "Scripted",
            ["Persistence:Provider"] = "InMemory",
            ["Harness:RetryBaseDelay"] = "00:00:00.010",
        };

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        if (chatClient is not null)
        {
            services.AddSingleton(chatClient);
        }

        services.AddProductionIncidentSystem(configuration, addWorker: false);
        return new TestSystem(services.BuildServiceProvider());
    }

    /// <summary>Starts an investigation and runs it until it is terminal or waiting for approval (no background worker).</summary>
    public async Task<InvestigationState> InvestigateAsync(string incidentId, string title, string service, string description = "", CancellationToken ct = default)
    {
        await Workflow.StartAsync(new IncidentRequest { IncidentId = incidentId, Title = title, Service = service, Description = description }, ct);
        return (await Workflow.RunAsync(incidentId, ct))!;
    }

    /// <summary>Decides every pending approval of an incident, then resumes the workflow to completion.</summary>
    public async Task<InvestigationState> DecideAllAsync(string incidentId, bool approve, CancellationToken ct = default)
    {
        foreach (var request in await Approvals.ListAsync(incidentId, ApprovalStatus.Pending, ct))
        {
            await Approvals.DecideAsync(request.Id, approve, "test-operator", approve ? "approved by test" : "rejected by test", ct);
        }

        var state = await Workflow.OnApprovalDecidedAsync(incidentId, ct);
        if (state is not null && state.Phase == InvestigationPhase.ExecutingActions)
        {
            state = await Workflow.RunAsync(incidentId, ct);
        }

        return state!;
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
