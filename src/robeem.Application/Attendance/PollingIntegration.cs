namespace robeem.Application.Attendance;

/// <summary>An active integration with a polling schedule.</summary>
/// <param name="IntegrationId">The integration.</param>
/// <param name="Cron">Its <c>PollingScheduleCron</c>.</param>
/// <param name="LastSuccessfulSyncAt">When it last synced successfully, as local wall-clock time; null if it never has.</param>
public sealed record PollingIntegration(int IntegrationId, string Cron, DateTime? LastSuccessfulSyncAt);
