using System.Threading.Channels;

namespace ProductionIncident.Application.Workflows;

/// <summary>Work queue between the gateway (API/webhook/Service Bus) and the workflow host.</summary>
public interface IInvestigationQueue
{
    ValueTask EnqueueAsync(string incidentId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed class ChannelInvestigationQueue : IInvestigationQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false });

    public ValueTask EnqueueAsync(string incidentId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(incidentId, cancellationToken);

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
