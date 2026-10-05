namespace robeem.Contracts.Attendance;

/// <summary>A pay period. While it is closed, payroll's extract returns none of its days.</summary>
public sealed class PayPeriodItem
{
    public int Id { get; init; }

    public DateTime StartDate { get; init; }

    /// <summary>Gets the last date of the period, inclusive.</summary>
    public DateTime EndDate { get; init; }

    public bool IsClosed { get; init; }
}
