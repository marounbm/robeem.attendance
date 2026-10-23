using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

/// <summary>What an HR administrator sets on an attendance policy. The limits are shared with the Api's own checks.</summary>
public sealed class PolicyInput
{
    public const int MaxRoundingMinutes = 60;
    public const int MaxGraceMinutes = 240;
    public const int MaxOvertimeThresholdMinutes = 480;
    public const int MaxDuplicateWindowSeconds = 3600;
    public const int MaxMissingPunchGraceMinutes = 1440;
    public const int MaxBreakMinutes = 240;

    /// <summary>The workflows a correction can follow, as the policy's <c>CorrectionApprovalWorkflow</c> names them.</summary>
    public static readonly IReadOnlyList<string> Workflows = ["Manager", "HR", "Both"];

    /// <summary>The interpretation modes an administrator can choose. <c>RulesEngine</c> exists in the model but has no engine yet.</summary>
    public static readonly IReadOnlyList<string> InterpretationModes = ["Off", "Sequential"];

    [Required]
    [StringLength(255)]
    [Display(Name = "Name")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the schedule the policy is for; null is the tenant-wide default. Fixed once created.</summary>
    [Display(Name = "Applies to")]
    public int? TimeScheduleId { get; set; }

    [Range(0, MaxRoundingMinutes)]
    [Display(Name = "Round punches to (minutes)")]
    public int RoundingRuleMinutes { get; set; }

    [Range(0, MaxGraceMinutes)]
    [Display(Name = "Late grace (minutes)")]
    public int LateGraceMinutes { get; set; }

    [Range(0, MaxGraceMinutes)]
    [Display(Name = "Early-departure grace (minutes)")]
    public int EarlyDepartureGraceMinutes { get; set; }

    [Range(0, MaxOvertimeThresholdMinutes)]
    [Display(Name = "Overtime starts after (minutes)")]
    public int OvertimeThresholdMinutes { get; set; }

    [Display(Name = "Overtime needs approval first")]
    public bool RequiresOvertimePreApproval { get; set; }

    [Required]
    [Display(Name = "Who approves corrections")]
    public string CorrectionApprovalWorkflow { get; set; } = "Manager";

    [Required]
    [Display(Name = "Unlabeled punches")]
    public string UnlabeledPunchInterpretationMode { get; set; } = "Off";

    [Range(1, MaxDuplicateWindowSeconds)]
    [Display(Name = "Duplicate window (seconds)")]
    public int DuplicateTimeWindowSeconds { get; set; } = 120;

    [Range(0, MaxMissingPunchGraceMinutes)]
    [Display(Name = "Missing-punch grace (minutes)")]
    public int MissingPunchGraceMinutes { get; set; } = 120;

    [Range(0, MaxBreakMinutes)]
    [Display(Name = "Ignore breaks shorter than (minutes)")]
    public int IgnoreBreaksShorterThanMinutes { get; set; } = 10;

    [Range(0, MaxBreakMinutes)]
    [Display(Name = "Break start allowance (minutes)")]
    public int BreakStartGraceMinutes { get; set; } = 15;

    [Range(0, MaxBreakMinutes)]
    [Display(Name = "Long break allowance (minutes)")]
    public int LongBreakGraceMinutes { get; set; } = 5;

    [Display(Name = "Long break raises an exception")]
    public bool RaiseExceptionOnLongBreak { get; set; }

    [Display(Name = "Late break raises an exception")]
    public bool RaiseExceptionOnLateBreak { get; set; }

    [Display(Name = "Early break raises an exception")]
    public bool RaiseExceptionOnEarlyBreak { get; set; }

    [Display(Name = "No break raises an exception")]
    public bool RaiseExceptionOnNoBreak { get; set; } = true;
}
