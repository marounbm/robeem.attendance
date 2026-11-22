namespace robeem.Contracts.HR.Employees;

public sealed class ScheduleOption
{
    public int Id { get; init; }

    public string Title { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the schedule is on demand: only employees paid by the hour can be assigned to it.</summary>
    public bool IsOnDemand { get; init; }
}
