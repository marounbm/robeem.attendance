namespace robeem.Tests.Attendance.Scenarios;

/// <summary>One employee's day: the shift they are scheduled for (if any), what they do, and the punches that come out of it.</summary>
internal sealed record DayPlan(DateTime Date, ScenarioShift? Shift, PlanKind Kind, IReadOnlyList<PlannedPunch> Punches);
