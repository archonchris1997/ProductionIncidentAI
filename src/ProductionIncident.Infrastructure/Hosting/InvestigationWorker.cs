using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionIncident.Application.Workflows;

namespace ProductionIncident.Infrastructure.Hosting;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int MaxConcurrentInvestigations { get; set; } = 4;

    public bool ResumeOnStartup { get; set; } = true;
}

/// <summary>Background host running investigations from the queue, with bounded parallelism and resume-on-startup.</summary>
public sealed class InvestigationWorker(
    IInvestigationQueue queue,
    IncidentWorkflow workflow,
    IOptions<WorkerOptions> options,
    ILogger<InvestigationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.ResumeOnStartup)
        {
            var resumed = await workflow.ResumeInterruptedAsync(stoppingToken).ConfigureAwait(false);
            if (resumed > 0)
            {
                logger.LogInformation("Resumed {Count} interrupted investigation(s) from checkpoints", resumed);
            }
        }

        using var slots = new SemaphoreSlim(Math.Max(1, options.Value.MaxConcurrentInvestigations));
        var running = new List<Task>();

        try
        {
            await foreach (var incidentId in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await slots.WaitAsync(stoppingToken).ConfigureAwait(false);
                running.RemoveAll(t => t.IsCompleted);
                running.Add(Task.Run(async () =>
                {
                    try
                    {
                        var state = await workflow.RunAsync(incidentId, stoppingToken).ConfigureAwait(false);
                        logger.LogInformation("Investigation {IncidentId} now {Phase}", incidentId, state?.Phase);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // shutting down: the checkpoint lets it resume on next start
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Investigation {IncidentId} crashed", incidentId);
                    }
                    finally
                    {
                        slots.Release();
                    }
                }, CancellationToken.None));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }
}
