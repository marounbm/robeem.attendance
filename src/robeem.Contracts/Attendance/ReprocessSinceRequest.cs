namespace robeem.Contracts.Attendance;

/// <summary>Asks for the attendance already calculated from a date on to be calculated again, after a policy, rules, holidays or leaves changed.</summary>
public sealed class ReprocessSinceRequest
{
    /// <summary>Gets or sets the first day to calculate again; the last is today.</summary>
    public DateTime From { get; set; }

    /// <summary>Gets or sets one employee to calculate again; null is everyone.</summary>
    public int? EmployeeId { get; set; }
}
