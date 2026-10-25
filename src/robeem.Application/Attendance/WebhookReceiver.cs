using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Application.Security;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class WebhookReceiver : IWebhookReceiver
{
    private const string BearerPrefix = "Bearer ";

    // Checked against when the connection has no secret, so a request to one takes as long as to any other.
    private const string NoSecret = "no secret: nothing can match";

    private readonly IRobeemDbContext _db;
    private readonly ISecretStore _secrets;
    private readonly IIngestionService _ingestion;

    public WebhookReceiver(IRobeemDbContext db, ISecretStore secrets, IIngestionService ingestion)
    {
        _db = db;
        _secrets = secrets;
        _ingestion = ingestion;
    }

    public async Task<WebhookReceipt> ReceiveAsync(int integrationId, int? connectionId, byte[] body, WebhookCredentials credentials, CancellationToken cancellationToken)
    {
        // A connector takes webhooks when it lists the Webhook transport; the integration, its connector and the vendor must all be active.
        var webhook = nameof(AttendanceTransport.Webhook);
        var integration = await _db.AttendanceIntegrations.AsNoTracking()
            .Where(i => i.Id == integrationId)
            .Select(i => new
            {
                i.Status,
                TakesWebhooks = i.Connector!.IsActive && i.Connector.Vendor!.IsActive && i.Connector.Transports.Any(t => t.Transport!.TransportKey == webhook),
            })
            .SingleOrDefaultAsync(cancellationToken);

        // The connection the route names; the old route names none, and means the default one if that is a webhook, else the first active webhook one.
        var connections = await _db.AttendanceConnections.AsNoTracking()
            .Where(c => c.IntegrationId == integrationId && c.Transport == AttendanceTransport.Webhook && c.IsActive)
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.IsDefault, c.AuthMethod, c.SecretRef })
            .ToListAsync(cancellationToken);
        var connection = connectionId is { } named
            ? connections.FirstOrDefault(c => c.Id == named)
            : connections.FirstOrDefault(c => c.IsDefault) ?? connections.FirstOrDefault();

        var accepts = integration is { Status: AttendanceIntegrationStatus.Active, TakesWebhooks: true } && connection is not null;
        var secret = accepts ? await _secrets.GetSecretAsync(connection!.SecretRef, cancellationToken) : null;

        // The credential is always checked, whatever else is wrong.
        var proven = Proves(connection?.AuthMethod, string.IsNullOrEmpty(secret) ? NoSecret : secret, body, credentials);
        if (!accepts || string.IsNullOrEmpty(secret) || !proven)
        {
            return new WebhookReceipt(WebhookOutcome.Unauthorized);
        }

        var path = Path.GetTempFileName();
        try
        {
            // The adapter reads from a path, as it does for an upload.
            await File.WriteAllBytesAsync(path, body, cancellationToken);
            var result = await _ingestion.IngestWebhookPayloadAsync(integrationId, connection!.Id, path, cancellationToken);
            if (result is null)
            {
                return new WebhookReceipt(WebhookOutcome.Unauthorized);
            }

            return result.Status == nameof(AttendanceBatchStatus.Failed)
                ? new WebhookReceipt(WebhookOutcome.Rejected, result, result.ErrorSummary)
                : new WebhookReceipt(WebhookOutcome.Accepted, result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool Proves(ConnectionAuthMethod? method, string secret, byte[] body, WebhookCredentials credentials) => method switch
    {
        ConnectionAuthMethod.HmacSignature => WebhookSignature.IsValid(secret, body, credentials.Signature),
        ConnectionAuthMethod.ApiKey => SameSecret(secret, credentials.ApiKey),
        ConnectionAuthMethod.BearerToken => SameSecret(
            secret,
            credentials.Authorization is { } header && header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase) ? header[BearerPrefix.Length..].Trim() : null),
        _ => false,
    };

    /// <summary>Compares two secrets in constant time (both hashed first, so their lengths do not show).</summary>
    private static bool SameSecret(string secret, string? presented) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(secret)),
            SHA256.HashData(Encoding.UTF8.GetBytes(presented ?? string.Empty))) && !string.IsNullOrEmpty(presented);
}
