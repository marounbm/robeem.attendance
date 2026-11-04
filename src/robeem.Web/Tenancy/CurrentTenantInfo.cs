using robeem.Web.ApiClients;

namespace robeem.Web.Tenancy;

/// <inheritdoc />
public sealed class CurrentTenantInfo : ICurrentTenantInfo
{
    private readonly ITenantsApiClient _tenants;
    private bool _resolved;

    public CurrentTenantInfo(ITenantsApiClient tenants)
    {
        _tenants = tenants;
    }

    public bool? IsPrime { get; private set; }

    public async Task ResolveAsync(CancellationToken cancellationToken)
    {
        if (_resolved)
        {
            return;
        }

        try
        {
            var current = await _tenants.GetCurrentAsync(cancellationToken);
            IsPrime = current?.IsPrime;
            _resolved = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TenantSelectionRequiredException)
        {
            // The Api is unreachable or rejected the tenant: stay unknown and ask again. The screen's own call reports a real problem.
        }
    }
}
