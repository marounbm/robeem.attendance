using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.HR.Schedules;

/// <summary>A shift of a time schedule. Times are <c>HH:mm</c>; an end earlier than the start is an overnight shift.</summary>
public sealed class ScheduleShiftInput
{
    [Required]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(5)]
    [Display(Name = "Starts")]
    public string StartTime { get; set; } = "09:00";

    [Required]
    [StringLength(5)]
    [Display(Name = "Ends")]
    public string EndTime { get; set; } = "17:00";

    /// <summary>Gets or sets a value indicating whether the shift has an unpaid break, which is taken out of the hours worked.</summary>
    [Display(Name = "Has a break")]
    public bool BreakTime { get; set; }

    [StringLength(5)]
    [Display(Name = "Break starts")]
    public string BreakStartTime { get; set; } = "13:00";

    [StringLength(5)]
    [Display(Name = "Break ends")]
    public string BreakEndTime { get; set; } = "14:00";

    /// <summary>Gets or sets a value indicating whether the shift is an overtime shift.</summary>
    [Display(Name = "Overtime shift")]
    public bool Overtime { get; set; }
}
