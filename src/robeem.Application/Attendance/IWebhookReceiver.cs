namespace robeem.Application.Attendance;

/// <summary>
/// Takes in a request pushed to an integration's webhook connection. It runs in the integration's tenant
/// scope. A request is accepted only when it proves itself as the connection's authentication method says
/// (an HMAC signature, an API key or a bearer token, under the connection's secret): a connection without a
/// secret accepts nothing (CLAUDE.md §2.8).
/// </summary>
public interface IWebhookReceiver
{
    /// <param name="integrationId">The integration the request names.</param>
    /// <param name="connectionId">The webhook connection the request names; null for the old route, which means the integration's default webhook connection.</param>
    /// <param name="body">The raw body, exactly as received: it is what a signature covers.</param>
    /// <param name="credentials">What the request presented.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<WebhookReceipt> ReceiveAsync(int integrationId, int? connectionId, byte[] body, WebhookCredentials credentials, CancellationToken cancellationToken);
}
