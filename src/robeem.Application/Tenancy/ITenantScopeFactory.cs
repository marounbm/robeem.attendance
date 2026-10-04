using Microsoft.Extensions.DependencyInjection;

namespace robeem.Application.Tenancy;

/// <summary>
/// Creates a DI scope bound to one tenant, for work that runs outside an HTTP request
/// (startup migration, seeding, background jobs). Services resolved from the returned scope,
/// including the DbContext, use that tenant's database.
/// </summary>
public interface ITenantScopeFactory
{
    AsyncServiceScope CreateScope(string tenantId);
}
