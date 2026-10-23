namespace robeem.Contracts.Attendance;

/// <summary>One attendance policy as the list shows it: where it applies and its settings.</summary>
public sealed class PolicyListItem
{
    public int Id { get; init; }

    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the schedule it applies to; null for the tenant-wide default.</summary>
    public int? TimeScheduleId { get; init; }

    public string? TimeScheduleTitle { get; init; }

    public bool IsTenantDefault { get; init; }

    public int RoundingRuleMinutes { get; init; }

    public int LateGraceMinutes { get; init; }

    public int EarlyDepartureGraceMinutes { get; init; }

    public int OvertimeThresholdMinutes { get; init; }

    public bool RequiresOvertimePreApproval { get; init; }

    public string CorrectionApprovalWorkflow { get; init; } = string.Empty;

    public string UnlabeledPunchInterpretationMode { get; init; } = string.Empty;

    public int DuplicateTimeWindowSeconds { get; init; }

    public int MissingPunchGraceMinutes { get; init; }

    public int IgnoreBreaksShorterThanMinutes { get; init; }

    public int BreakStartGraceMinutes { get; init; }

    public int LongBreakGraceMinutes { get; init; }

    public bool RaiseExceptionOnLongBreak { get; init; }

    public bool RaiseExceptionOnLateBreak { get; init; }

    public bool RaiseExceptionOnEarlyBreak { get; init; }

    public bool RaiseExceptionOnNoBreak { get; init; }
}
