namespace robeem.Application.Attendance;

/// <summary>The actions of the attendance audit trail, as the <c>Action</c> of an entry.</summary>
public static class AttendanceAuditActions
{
    public const string CorrectionDecided = "CorrectionDecided";

    public const string ExceptionStatusChanged = "ExceptionStatusChanged";

    public const string ReviewDecided = "ReviewDecided";

    /// <summary>An integration, one of its devices or one of its vendor-id mappings was created, changed or removed.</summary>
    public const string IntegrationChanged = "IntegrationChanged";

    /// <summary>A sync or reconciliation was started by a person instead of the schedule.</summary>
    public const string IntegrationRunRequested = "IntegrationRunRequested";

    /// <summary>Raw or normalized attendance read outside normalization and processing.</summary>
    public const string AttendanceDataRead = "AttendanceDataRead";
}
