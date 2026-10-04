using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using robeem.DataModel;

namespace robeem.Infrastructure.Tenancy;

/// <summary>Validated, immutable view of the configured tenants. Fails fast on bad configuration.</summary>
public sealed class TenantRegistry : ITenantRegistry
{
    private static readonly Regex TenantIdPattern = new(
        $"^[a-z0-9][a-z0-9-]{{0,{ITenantScoped.TenantIdMaxLength - 1}}}$",
        RegexOptions.CultureInvariant);

    private readonly Dictionary<string, TenantDefinition> _tenants;

    public TenantRegistry(IOptions<TenancyOptions> options)
    {
        var value = options.Value;
        var errors = new List<string>();
        _tenants = new Dictionary<string, TenantDefinition>(StringComparer.OrdinalIgnoreCase);

        if (value.Tenants.Count == 0)
        {
            errors.Add("No tenants are configured.");
        }

        foreach (var tenant in value.Tenants)
        {
            if (!TenantIdPattern.IsMatch(tenant.Id))
            {
                errors.Add($"Tenant id '{tenant.Id}' must be lower-case letters, digits or '-', up to {ITenantScoped.TenantIdMaxLength} characters.");
            }
            else if (!_tenants.TryAdd(tenant.Id, tenant))
            {
                errors.Add($"Tenant id '{tenant.Id}' is configured more than once.");
            }

            if (string.IsNullOrWhiteSpace(tenant.ConnectionString))
            {
                errors.Add($"Tenant '{tenant.Id}' has no connection string.");
            }
        }

        if (value.DefaultTenantId is not null)
        {
            DefaultTenant = Find(value.DefaultTenantId);
            if (DefaultTenant is null)
            {
                errors.Add($"Default tenant '{value.DefaultTenantId}' is not a configured tenant.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid '{TenancyOptions.SectionName}' configuration: {string.Join(" ", errors)}");
        }

        Tenants = value.Tenants.AsReadOnly();
    }

    public IReadOnlyList<TenantDefinition> Tenants { get; }

    public TenantDefinition? DefaultTenant { get; }

    public TenantDefinition? Find(string? tenantId)
        => tenantId is not null && _tenants.TryGetValue(tenantId.Trim(), out var tenant) ? tenant : null;
}
