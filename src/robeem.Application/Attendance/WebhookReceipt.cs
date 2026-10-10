using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>The answer to a webhook request: the stored batch when it was accepted, or why the body was unusable when it was rejected.</summary>
public sealed record WebhookReceipt(WebhookOutcome Outcome, AttendanceIngestionResult? Result = null, string? Message = null);
