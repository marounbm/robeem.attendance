using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace robeem.Application.Attendance;

/// <summary>
/// The Polly pipeline of each integration (study §12.4): a circuit breaker around a retry with
/// exponential backoff and jitter. A pipeline is kept per tenant and integration, so one failing
/// vendor never slows or stops another. Register as a singleton: the breaker state lives here.
/// </summary>
/// <remarks>
/// The breaker is the outer layer, so one failed sync, after all its retries, is one sample: the
/// threshold counts syncs, not attempts. Exceptions that say nothing about the vendor's health are
/// not handled: cancellation, and <see cref="NotSupportedException"/> (an adapter without
/// incremental sync).
/// </remarks>
public sealed class IntegrationResilience
{
    private readonly ConcurrentDictionary<(string TenantId, int IntegrationId), IntegrationPipeline> _pipelines = new();
    private readonly AttendanceSyncOptions _options;
    private readonly TimeProvider _clock;

    public IntegrationResilience(IOptions<AttendanceSyncOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public IntegrationPipeline Get(string tenantId, int integrationId) =>
        _pipelines.GetOrAdd((tenantId, integrationId), _ => Build());

    private static ValueTask<bool> ShouldHandle(Exception? exception) =>
        ValueTask.FromResult(exception is not null and not OperationCanceledException and not NotSupportedException);

    private IntegrationPipeline Build()
    {
        var breaker = new CircuitBreakerStateProvider();
        var pipeline = new ResiliencePipelineBuilder { TimeProvider = _clock }
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                // Every sample in the window must be a failure: a success in between keeps it closed.
                FailureRatio = 1.0,
                MinimumThroughput = Math.Max(2, _options.BreakerFailureThreshold),
                SamplingDuration = TimeSpan.FromMinutes(Math.Max(1, _options.BreakerSamplingMinutes)),
                BreakDuration = TimeSpan.FromSeconds(Math.Max(0.5, _options.BreakerBreakSeconds)),
                ShouldHandle = args => ShouldHandle(args.Outcome.Exception),
                StateProvider = breaker,
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Math.Max(0, _options.RetryAttempts),
                Delay = TimeSpan.FromSeconds(Math.Max(0, _options.RetryDelaySeconds)),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ShouldHandle(args.Outcome.Exception),
            })
            .Build();

        return new IntegrationPipeline(pipeline, breaker);
    }
}
