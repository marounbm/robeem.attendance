namespace robeem.Contracts.Attendance;

/// <summary>Asks for the days nobody punched on to be looked for in a period, so that a no-show is raised for each scheduled day that was missed.</summary>
public sealed class CheckMissingDaysRequest
{
    /// <summary>The longest period one request can cover, in days.</summary>
    public const int MaxDays = 366;

    public DateTime From { get; set; }

    public DateTime To { get; set; }

    /// <summary>Gets or sets one employee to look at; null is everyone.</summary>
    public int? EmployeeId { get; set; }
}
