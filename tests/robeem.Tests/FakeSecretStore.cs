using robeem.Application.Security;

namespace robeem.Tests;

/// <summary>A secret store with the secrets a test sets.</summary>
public sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = [];

    public void Set(string reference, string secret) => _secrets[reference] = secret;

    public Task<string?> GetSecretAsync(string? reference, CancellationToken cancellationToken) =>
        Task.FromResult(reference is not null && _secrets.TryGetValue(reference, out var secret) ? secret : null);
}
