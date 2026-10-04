namespace robeem.Application.Attendance;

/// <summary>
/// The adapter for one attendance vendor (study §5.3, §12.1). Adding a vendor means adding one
/// implementation, registered as a keyed service under its <c>AttendanceProvider.ProviderKey</c>
/// (CLAUDE.md §1.9). An adapter copies only the fields it knows (an allowlist, CLAUDE.md §1.10)
/// and never interprets a punch's meaning.
/// </summary>
public interface IAttendanceProvider
{
    Task<ConnectionValidationResult> ValidateConnectionAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken);

    Task<AuthResult> AuthenticateAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExternalDeviceDto>> RetrieveDevicesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExternalEmployeeDto>> RetrieveEmployeesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken);

    /// <summary>Retrieves the transactions in <paramref name="range"/>, or every available one when it is null.</summary>
    Task<AttendanceBatchResult> RetrieveAttendanceTransactionsAsync(AttendanceIntegrationConfig config, DateRange? range, CancellationToken cancellationToken);

    /// <summary>Retrieves the transactions after <paramref name="cursor"/>, or from the beginning when it is null.</summary>
    Task<AttendanceBatchResult> GetTransactionChangesAsync(AttendanceIntegrationConfig config, SyncCursor? cursor, CancellationToken cancellationToken);
}
