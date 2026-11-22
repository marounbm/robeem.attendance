using robeem.Contracts.Attendance;

namespace robeem.Tests.Attendance.Scenarios;

/// <summary>What is in force when a day is calculated: the standard rules, and the two policy settings that are not rules.</summary>
internal sealed record OracleSettings(RuleSettings Rules, bool Sequential, bool PreApproval);
