namespace robeem.Contracts.HR.Schedules;

/// <summary>The employees of a team, replacing who was in it. Anyone left out goes back to the everyone-team.</summary>
public sealed class ScheduleMembersInput
{
    public List<int> EmployeeIds { get; set; } = [];
}
