namespace robeem.Application.Attendance;

/// <summary>The actions of the attendance audit trail, as the <c>Action</c> of an entry.</summary>
public static class AttendanceAuditActions
{
    public const string CorrectionDecided = "CorrectionDecided";

    public const string ExceptionStatusChanged = "ExceptionStatusChanged";

    public const string ReviewDecided = "ReviewDecided";

    /// <summary>An integration, one of its devices or one of its vendor-id mappings was created, changed or removed.</summary>
    public const string IntegrationChanged = "IntegrationChanged";

    /// <summary>A person tested a connection. The entry says whether it worked, never what the other end answered.</summary>
    public const string ConnectionTested = "ConnectionTested";

    /// <summary>A pay period was created, closed or reopened.</summary>
    public const string PayPeriodChanged = "PayPeriodChanged";

    /// <summary>An attendance policy was created, changed or deleted.</summary>
    public const string PolicyChanged = "PolicyChanged";

    /// <summary>A time schedule, its shifts, teams or rosters were changed: they decide the hours every day is calculated against.</summary>
    public const string ScheduleChanged = Common.ChangeAuditActions.ScheduleChanged;

    /// <summary>HR ran the processing engine again for one employee's day.</summary>
    public const string DayReprocessed = "DayReprocessed";

    /// <summary>A sync or reconciliation was started by a person instead of the schedule.</summary>
    public const string IntegrationRunRequested = "IntegrationRunRequested";

    /// <summary>Raw or normalized attendance read outside normalization and processing.</summary>
    public const string AttendanceDataRead = "AttendanceDataRead";
}
