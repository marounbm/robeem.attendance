namespace robeem.Contracts.Attendance;

/// <summary>Closes one employee's pay period. It is refused while the period has not ended or the employee has days that are not ready.</summary>
public sealed class CompensationCloseRequest
{
    public int EmployeeId { get; init; }

    /// <summary>Gets the first day of the period to close.</summary>
    public DateTime PeriodStart { get; init; }
}
