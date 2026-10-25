using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// The connections of an integration: where its attendance comes from and how that is authenticated. Every change is audited
/// and holds the connection to what its transport declares (<see cref="TransportDescriptors"/>). Nothing here returns a credential
/// or what the other end answered.
/// </summary>
public interface IAttendanceConnectionService
{
    /// <summary>Gets the connections of an integration, the default first; null when the integration does not exist.</summary>
    Task<IReadOnlyList<ConnectionListItem>?> ListAsync(int integrationId, CancellationToken cancellationToken);

    Task<ConnectionInput?> GetInputAsync(int integrationId, int connectionId, CancellationToken cancellationToken);

    /// <summary>Adds a connection. It is the integration's default only when the integration has none yet.</summary>
    Task<SaveResult> CreateAsync(int integrationId, ConnectionInput input, CancellationToken cancellationToken);

    Task<SaveResult> UpdateAsync(int integrationId, int connectionId, ConnectionInput input, CancellationToken cancellationToken);

    /// <summary>Makes a connection the integration's default; the previous default is not. The connection must be active.</summary>
    Task<SaveResult> MakeDefaultAsync(int integrationId, int connectionId, CancellationToken cancellationToken);

    /// <summary>
    /// Tests a connection and records the outcome on it. Audited. Null when the connection does not exist. A transport with no adapter
    /// yet answers that it can't be tested.
    /// </summary>
    Task<ConnectionTestResult?> TestAsync(int integrationId, int connectionId, CancellationToken cancellationToken);

    /// <summary>Gets what each transport declares: its settings and the authentication it allows.</summary>
    IReadOnlyList<TransportDescriptorItem> GetDescriptors();
}
