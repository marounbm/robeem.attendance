using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Application.Security;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class WebhookReceiver : IWebhookReceiver
{
    // Checked against when the integration has no secret, so a request to one takes as long as to any other.
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

    public async Task<WebhookReceipt> ReceiveAsync(int integrationId, byte[] body, string? signature, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations.AsNoTracking()
            .Include(i => i.Provider)
            .SingleOrDefaultAsync(i => i.Id == integrationId, cancellationToken);

        var accepts = integration is { Status: AttendanceIntegrationStatus.Active, Provider: { IsActive: true, SupportsWebhooks: true } };
        var secret = accepts ? await _secrets.GetSecretAsync(integration!.ConnectionConfigRef, cancellationToken) : null;

        // The signature is always checked, whatever else is wrong.
        var signed = WebhookSignature.IsValid(string.IsNullOrEmpty(secret) ? NoSecret : secret, body, signature);
        if (!accepts || string.IsNullOrEmpty(secret) || !signed)
        {
            return new WebhookReceipt(WebhookOutcome.Unauthorized);
        }

        var path = Path.GetTempFileName();
        try
        {
            // The adapter reads from a path, as it does for an upload.
            await File.WriteAllBytesAsync(path, body, cancellationToken);
            var result = await _ingestion.IngestWebhookPayloadAsync(integrationId, path, cancellationToken);
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
}
