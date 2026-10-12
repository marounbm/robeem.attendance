using robeem.DataModel;

namespace robeem.Tests.Api;

/// <summary>One history row as <see cref="InMemoryRecorder"/> holds it.</summary>
internal sealed record RecordedRun(int Id, string TenantId, string JobName, JobTrigger Trigger, JobRunStatus Status, DateTime? ScheduledFor, DateTime StartedAt, DateTime? CompletedAt, int WorkItems, string? Summary, int? RequestedById);
