namespace robeem.Application.Attendance;

/// <summary>Why a record waits for a person's review: the first that applies, in this order of precedence.</summary>
public enum ReviewReason
{
    /// <summary>Overtime that needs pre-approval.</summary>
    Overtime,

    /// <summary>Work on a day with no shift.</summary>
    UnscheduledWork,

    /// <summary>A scheduled day without both a check-in and a check-out.</summary>
    IncompleteDay,

    /// <summary>None of the above: left for review by the policy.</summary>
    Review,
}
