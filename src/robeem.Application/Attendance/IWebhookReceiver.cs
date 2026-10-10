namespace robeem.Application.Attendance;

/// <summary>
/// Takes in a request pushed to an integration's webhook. It runs in the integration's tenant
/// scope. A request is accepted only with a valid signature under the integration's secret: an
/// integration without a secret accepts nothing (CLAUDE.md §2.8).
/// </summary>
public interface IWebhookReceiver
{
    /// <param name="integrationId">The integration the request names.</param>
    /// <param name="body">The raw body, exactly as received: it is what was signed.</param>
    /// <param name="signature">The signature header, or null when none was sent.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<WebhookReceipt> ReceiveAsync(int integrationId, byte[] body, string? signature, CancellationToken cancellationToken);
}
