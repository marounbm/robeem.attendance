namespace robeem.Api.Attendance.Jobs;

/// <summary>What the scheduler knows about one job right now, in server local time.</summary>
public sealed record JobState(string Name, bool IsRunning, DateTime? NextRunAt, DateTime? LastCheckedAt);
