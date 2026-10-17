using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// The tenant's catalog of attendance providers. A provider is deactivated, never deleted: integrations and
/// raw punches point at it. A provider is usable when it is active and something can read what it delivers:
/// a default file format, or a code adapter registered under its key.
/// </summary>
public interface IAttendanceProviderService
{
    Task<IReadOnlyList<ProviderListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Gets a provider as editable input, or null when it does not exist.</summary>
    Task<ProviderInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<SaveResult> CreateAsync(ProviderInput input, CancellationToken cancellationToken);

    /// <summary>Updates a provider. Its key can't change: a code adapter and the raw punches it produced are tied to it.</summary>
    Task<SaveResult> UpdateAsync(int id, ProviderInput input, CancellationToken cancellationToken);
}
