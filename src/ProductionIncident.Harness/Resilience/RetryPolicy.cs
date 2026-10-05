using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ProductionIncident.Harness.Resilience;

/// <summary>Timeout + bounded exponential retry for transient failures of LLM and tool calls.</summary>
public sealed class RetryPolicy(IOptions<HarnessOptions> options, ILogger<RetryPolicy> logger)
{
    private readonly HarnessOptions _options = options.Value;

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        TimeSpan timeout,
        string operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            try
            {
                return await action(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= _options.MaxRetries)
                {
                    throw new TimeoutException($"{operation} timed out after {timeout.TotalSeconds:0}s ({attempt + 1} attempts).");
                }

                logger.LogWarning("{Operation} timed out (attempt {Attempt}); retrying", operation, attempt + 1);
            }
            catch (Exception ex) when (attempt < _options.MaxRetries && IsTransient(ex) && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "{Operation} failed transiently (attempt {Attempt}); retrying", operation, attempt + 1);
            }

            await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(_options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt) + Random.Shared.Next(0, 100));

    public static bool IsTransient(Exception ex)
    {
        if (ex is HttpRequestException or TimeoutException or IOException)
        {
            return true;
        }

        // System.ClientModel.ClientResultException (OpenAI / Azure SDKs) exposes an int Status.
        if (ex.GetType().GetProperty("Status")?.GetValue(ex) is int status)
        {
            return status == 408 || status == 429 || status >= 500;
        }

        return ex.InnerException is not null && IsTransient(ex.InnerException);
    }
}
