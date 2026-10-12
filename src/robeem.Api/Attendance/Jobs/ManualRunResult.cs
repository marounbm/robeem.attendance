namespace robeem.Api.Attendance.Jobs;

/// <summary>The answer to a manual run request; <see cref="RunId"/> is the history row, when it could be written.</summary>
public sealed record ManualRunResult(ManualRunOutcome Outcome, int? RunId = null);
