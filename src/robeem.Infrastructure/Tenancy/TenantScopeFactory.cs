using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Tenancy;

namespace robeem.Infrastructure.Tenancy;

/// <summary>
/// Creates a DI scope bound to one tenant, for work that runs outside an HTTP request
/// (startup migration, seeding, background jobs). Services resolved from the returned scope,
/// including the DbContext, use that tenant's database.
/// </summary>
public sealed class TenantScopeFactory : ITenantScopeFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TenantScopeFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public AsyncServiceScope CreateScope(string tenantId)
    {
        var scope = _scopeFactory.CreateAsyncScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
