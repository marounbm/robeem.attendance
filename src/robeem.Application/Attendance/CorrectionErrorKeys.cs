namespace robeem.Application.Attendance;

/// <summary>
/// The error keys of a refused correction that concern who acts. The actor is the authenticated
/// user, so these are no longer request properties; the keys keep their names for the callers
/// that read them.
/// </summary>
public static class CorrectionErrorKeys
{
    public const string RequestedBy = "RequestedByEmployeeId";

    public const string DecidedBy = "DecidedByUserId";
}
