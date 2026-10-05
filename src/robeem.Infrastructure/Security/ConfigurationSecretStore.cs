using Microsoft.Extensions.Configuration;
using robeem.Application.Security;
using robeem.Application.Tenancy;

namespace robeem.Infrastructure.Security;

/// <summary>
/// Reads a tenant's secrets from configuration, under <c>Secrets:{tenantId}:{reference}</c>.
/// Configuration is whatever the host layers on: user-secrets in Development; in production
/// environment variables, or a vault such as Azure Key Vault added as a configuration provider
/// (<c>Secrets--tenant--reference</c> as its secret name). Nothing here changes when it does.
/// </summary>
public sealed class ConfigurationSecretStore : ISecretStore
{
    public const string SectionName = "Secrets";

    private readonly IConfiguration _configuration;
    private readonly ITenantContext _tenant;

    public ConfigurationSecretStore(IConfiguration configuration, ITenantContext tenant)
    {
        _configuration = configuration;
        _tenant = tenant;
    }

    public Task<string?> GetSecretAsync(string? reference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return Task.FromResult<string?>(null);
        }

        // Fails closed like the DbContext: a secret is never looked up without a tenant.
        var tenantId = _tenant.TenantId ?? throw new TenantNotResolvedException();
        return Task.FromResult(_configuration[$"{SectionName}:{tenantId}:{reference.Trim()}"]);
    }
}
