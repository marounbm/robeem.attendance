using Microsoft.Extensions.Hosting;

namespace robeem.Tests.Api;

/// <summary>An application lifetime whose stopping token the test controls.</summary>
internal sealed class FakeLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _stopping = new();

    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => _stopping.Token;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication() => _stopping.Cancel();
}
