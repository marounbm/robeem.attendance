namespace robeem.Application.Attendance;

/// <summary>The actions of the attendance audit trail, as the <c>Action</c> of an entry.</summary>
public static class AttendanceAuditActions
{
    public const string CorrectionDecided = "CorrectionDecided";

    public const string ExceptionStatusChanged = "ExceptionStatusChanged";

    public const string ReviewDecided = "ReviewDecided";

    /// <summary>Raw or normalized attendance read outside normalization and processing.</summary>
    public const string AttendanceDataRead = "AttendanceDataRead";
}
