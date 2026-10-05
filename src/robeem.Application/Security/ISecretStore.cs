namespace robeem.Application.Security;

/// <summary>
/// Resolves a secret reference, such as <c>AttendanceIntegration.ConnectionConfigRef</c>, to the
/// secret itself. References are per tenant: the same reference in two tenants is two secrets.
/// A secret is used for the call that needs it and never logged or stored in a table.
/// </summary>
public interface ISecretStore
{
    /// <summary>Gets the secret the reference names in the current tenant; null when there is none (or the reference is empty).</summary>
    Task<string?> GetSecretAsync(string? reference, CancellationToken cancellationToken);
}
