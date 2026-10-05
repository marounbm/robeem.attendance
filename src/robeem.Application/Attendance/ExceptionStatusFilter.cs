namespace robeem.Application.Attendance;

public enum ExceptionStatusFilter
{
    /// <summary>Open and PendingCorrection: the ones a person still has to act on.</summary>
    Active,
    Open,
    PendingCorrection,
    Closed,
    All,
}
