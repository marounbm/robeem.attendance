using Polly;
using Polly.CircuitBreaker;

namespace robeem.Application.Attendance;

/// <summary>One integration's resilience pipeline and the state of its circuit breaker.</summary>
public sealed class IntegrationPipeline
{
    private readonly CircuitBreakerStateProvider _breaker;

    internal IntegrationPipeline(ResiliencePipeline pipeline, CircuitBreakerStateProvider breaker)
    {
        Pipeline = pipeline;
        _breaker = breaker;
    }

    public ResiliencePipeline Pipeline { get; }

    /// <summary>Gets a value indicating whether the breaker refuses calls at the moment.</summary>
    public bool IsOpen => _breaker.CircuitState is CircuitState.Open or CircuitState.Isolated;
}
