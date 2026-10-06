using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.HR.Schedules;

/// <summary>A team of a time schedule: a group of employees that works its own roster in addition to the everyone-team's.</summary>
public sealed class ScheduleTeamInput
{
    [Required]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;
}
