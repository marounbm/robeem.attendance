namespace robeem.Application.Tenancy;

/// <summary>Tells whether the scope's tenant is the prime tenant, which alone may change the platform catalog.</summary>
public interface IPrimeTenantContext
{
    /// <summary>Gets the id of the prime tenant.</summary>
    string PrimeTenantId { get; }

    /// <summary>Gets a value indicating whether the scope's tenant is the prime tenant. <c>false</c> when no tenant is resolved.</summary>
    bool IsCurrentTenantPrime { get; }
}
