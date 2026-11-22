namespace robeem.Application.Attendance.Seeding;

/// <summary>An employee made for one case on one schedule (and team).</summary>
public sealed record ScenarioEmployee(string Number, string FirstName, string LastName, string Department, string ScheduleTitle, string? TeamTitle, ScenarioCase Case);
