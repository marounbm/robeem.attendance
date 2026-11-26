namespace robeem.Contracts.Attendance;

/// <summary>What happens to a day correction a manager or HR sends on an employee's behalf (<see cref="DayCorrectionView.OnBehalfOutcome"/>).</summary>
public static class DayCorrectionOutcome
{
    /// <summary>The sender holds every step of the approval, so the correction is applied and the day calculated again at once.</summary>
    public const string AppliedAtOnce = "AppliedAtOnce";

    /// <summary>The employee's manager still has to decide.</summary>
    public const string AwaitsManager = "AwaitsManager";

    /// <summary>HR still has to decide.</summary>
    public const string AwaitsHr = "AwaitsHr";
}
