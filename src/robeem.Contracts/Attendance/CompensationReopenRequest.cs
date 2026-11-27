namespace robeem.Contracts.Attendance;

/// <summary>Opens an employee's closed pay period again, with a reason. Its decisions can then be changed and payroll reads its days again.</summary>
public sealed class CompensationReopenRequest
{
    public int EmployeeId { get; init; }

    /// <summary>Gets the first day of the period to reopen.</summary>
    public DateTime PeriodStart { get; init; }

    public string Reason { get; init; } = string.Empty;
}
