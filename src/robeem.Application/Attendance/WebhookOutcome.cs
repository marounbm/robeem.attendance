namespace robeem.Application.Attendance;

/// <summary>What became of a webhook request. <c>Unauthorized</c> does not say why: no integration, not a webhook, not active, no secret and a wrong signature look the same.</summary>
public enum WebhookOutcome
{
    Accepted,
    Unauthorized,
    Rejected,
}
