namespace robeem.Application.Attendance.Seeding;

/// <summary>One kind of employee the scenario data has: how that person behaves on the days they are scheduled.</summary>
/// <param name="Code">A short upper-case code, part of the employee number.</param>
/// <param name="Title">What the person does, in words; the employee's last name.</param>
/// <param name="Description">What the attendance should make of it, in words.</param>
/// <param name="NeedsBreak">Whether the case only makes sense on a shift that has a scheduled break.</param>
/// <param name="Hourly">Whether the case is for employees paid by the hour (on an on-demand schedule) and no other.</param>
public sealed record ScenarioCase(string Code, string Title, string Description, bool NeedsBreak = false, bool Hourly = false);
