using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.HR.Schedules;

/// <summary>The settings of a time schedule: its name and how its pattern repeats.</summary>
public sealed class TimeScheduleInput
{
    [Required]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    /// <summary>Gets or sets the length of the repeating pattern in days: 7 for a weekly pattern, 14 for two weeks, and so on.</summary>
    [Range(1, 56)]
    [Display(Name = "Pattern length (days)")]
    public int CyclePeriod { get; set; } = 7;

    /// <summary>Gets or sets the weekday the first day of a weekly (7-day) pattern falls on, as a <c>DayOfWeek</c> name. Used only when the pattern is 7 days long.</summary>
    [Display(Name = "Week starts on")]
    public string FirstCycleDay { get; set; } = nameof(DayOfWeek.Monday);

    /// <summary>Gets or sets the date that is day 1 of the pattern. Required when the pattern is not 7 days long.</summary>
    [DataType(DataType.Date)]
    [Display(Name = "Day 1 falls on")]
    public DateTime? CycleStartDate { get; set; }
}
