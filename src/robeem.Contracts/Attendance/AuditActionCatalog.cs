namespace robeem.Contracts.Attendance;

/// <summary>
/// The actions an audit entry can carry, for the audit screen's filter. Kept equal to the Application's
/// <c>AttendanceAuditActions</c> by a test, since the Web can't reference Application.
/// </summary>
public static class AuditActionCatalog
{
    public static readonly IReadOnlyList<string> All =
    [
        "CorrectionDecided",
        "ExceptionStatusChanged",
        "ReviewDecided",
        "IntegrationChanged",
        "IntegrationRunRequested",
        "ConnectionTested",
        "PayPeriodChanged",
        "PolicyChanged",
        "DayReprocessed",
        "ScheduleChanged",
        "AttendanceDataRead",
    ];
}
