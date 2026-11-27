namespace robeem.Contracts.Attendance;

/// <summary>An employee whose pay period was not closed by "close all", and why.</summary>
public sealed class CompensationSkippedEmployee
{
    public int EmployeeId { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string EmployeeName { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;
}
