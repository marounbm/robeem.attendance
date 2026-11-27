namespace robeem.Contracts.Attendance;

/// <summary>An employee and the pay cycle that applies to them: their own, else the tenant default, else the calendar month.</summary>
public sealed class EmployeePayCycleItem
{
    public int EmployeeId { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>Gets where the cycle comes from: <c>Own</c>, <c>Default</c> or <c>Calendar month</c> (nothing set).</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Gets the cycle that applies, with the periods it gives around today.</summary>
    public PayCycleItem Cycle { get; init; } = new();
}
