namespace robeem.Application.Attendance;

/// <summary>The actions of the attendance audit trail, as the <c>Action</c> of an entry.</summary>
public static class AttendanceAuditActions
{
    public const string CorrectionDecided = "CorrectionDecided";

    public const string ExceptionStatusChanged = "ExceptionStatusChanged";

    /// <summary>A group of related exceptions (an employee's shift on a date) changed status: it follows its exceptions, or a manager or HR accepted it as a whole.</summary>
    public const string ExceptionGroupStatusChanged = "ExceptionGroupStatusChanged";

    public const string ReviewDecided = "ReviewDecided";

    /// <summary>An integration, one of its devices or one of its vendor-id mappings was created, changed or removed.</summary>
    public const string IntegrationChanged = "IntegrationChanged";

    /// <summary>A person tested a connection. The entry says whether it worked, never what the other end answered.</summary>
    public const string ConnectionTested = "ConnectionTested";

    /// <summary>A pay period was created, closed or reopened.</summary>
    public const string PayPeriodChanged = "PayPeriodChanged";

    /// <summary>The tenant's default pay cycle, or an employee's own, was set or removed.</summary>
    public const string PayCycleChanged = "PayCycleChanged";

    /// <summary>A payroll administrator decided a line of an employee's compensation report (overtime or under time), possibly adding leave to the employee's leaves.</summary>
    public const string CompensationDecided = "CompensationDecided";

    /// <summary>The tenant's attendance settings (the date the punching started) were changed.</summary>
    public const string AttendanceSettingsChanged = "AttendanceSettingsChanged";

    /// <summary>An attendance policy was created, changed or deleted.</summary>
    public const string PolicyChanged = "PolicyChanged";

    /// <summary>A time schedule, its shifts, teams or rosters were changed: they decide the hours every day is calculated against.</summary>
    public const string ScheduleChanged = Common.ChangeAuditActions.ScheduleChanged;

    /// <summary>HR ran the processing engine again for one employee's day.</summary>
    public const string DayReprocessed = "DayReprocessed";

    /// <summary>A sync or reconciliation was started by a person instead of the schedule.</summary>
    public const string IntegrationRunRequested = "IntegrationRunRequested";

    /// <summary>A failed or repeated upload was deleted with its never-processed punches. The only time raw attendance is deleted.</summary>
    public const string UploadDeleted = "UploadDeleted";

    /// <summary>An administrator deleted all of a tenant's attendance data to import it again. The widest deletion of raw attendance; the entry lists how much of each kind went and is itself kept.</summary>
    public const string AttendanceDataReset = "AttendanceDataReset";

    /// <summary>An administrator loaded test employees from a file: how many were made, put on a schedule and mapped to a device, and any device made for them.</summary>
    public const string TestEmployeesLoaded = "TestEmployeesLoaded";

    /// <summary>An administrator deleted employees named in a file, with all their attendance data (raw punches included). The entry lists the employees and how much of each kind went, and is itself kept.</summary>
    public const string TestEmployeesDeleted = "TestEmployeesDeleted";

    /// <summary>Raw or normalized attendance read outside normalization and processing.</summary>
    public const string AttendanceDataRead = "AttendanceDataRead";

    /// <summary>A set of rules that decide how breaks are read and which findings become exceptions was published. The entry lists any rule that suppresses a finding, with its reason.</summary>
    public const string RuleSetPublished = "RuleSetPublished";
}
