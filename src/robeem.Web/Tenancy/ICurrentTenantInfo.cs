namespace robeem.Web.Tenancy;

/// <summary>
/// What the screens need to know about the tenant the request works for. Only to decide what to <i>show</i>: the Api is the authority
/// and refuses a change to the platform catalog from any tenant but the prime one.
/// </summary>
public interface ICurrentTenantInfo
{
    /// <summary>Gets a value indicating whether this is the prime tenant, which owns the platform catalog; null while that is not known (the Api could not be asked).</summary>
    bool? IsPrime { get; }

    /// <summary>Asks the Api which tenant the request resolves to, once per request. Never throws for an unreachable Api.</summary>
    Task ResolveAsync(CancellationToken cancellationToken);
}
