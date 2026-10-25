using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Tenancy;
using robeem.Application.Attendance;
using robeem.Application.Tenancy;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

/// <summary>
/// The inbound webhook of the <c>generic-webhook</c> vendor. The one attendance endpoint with no
/// signed-in user: the sender is not a person and has no token. It authenticates by a signature
/// instead (<see cref="SignatureHeader"/>, HMAC-SHA256 of the raw body under the integration's secret).
/// </summary>
/// <remarks>
/// <para>The tenant comes from the route, not from a claim, so the request runs in its own tenant
/// scope (<see cref="ITenantScopeFactory"/>) and the request's own tenant, if any, is not used.
/// An unknown tenant, an unknown or inactive integration, one that is not a webhook, one without a
/// secret and a wrong signature all answer the same 401, so the route cannot be used to find out which
/// tenants or integrations exist. Nothing is accepted unsigned.</para>
/// <para>A replayed request stores its rows again; duplicate detection at normalization absorbs them
/// (CLAUDE.md §1.4).</para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[AllowWithoutTenant]
[Route("api/attendance/webhooks")]
public sealed class AttendanceWebhookController : ControllerBase
{
    public const string SignatureHeader = "X-Signature";

    /// <summary>The header a connection authenticated by an API key presents it in.</summary>
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>The largest body accepted.</summary>
    public const int MaxBodyBytes = 1024 * 1024;

    private readonly ITenantRegistry _tenants;
    private readonly ITenantScopeFactory _scopes;

    public AttendanceWebhookController(ITenantRegistry tenants, ITenantScopeFactory scopes)
    {
        _tenants = tenants;
        _scopes = scopes;
    }

    /// <summary>Takes the pushed events into the integration's raw transactions through its default webhook connection: 202 with the batch counts.</summary>
    [HttpPost("{tenantId}/{integrationId:int}")]
    public Task<ActionResult<AttendanceIngestionResult>> Receive(string tenantId, int integrationId, CancellationToken cancellationToken) =>
        ReceiveAsync(tenantId, integrationId, null, cancellationToken);

    /// <summary>As <see cref="Receive"/>, through the named webhook connection of the integration.</summary>
    [HttpPost("{tenantId}/{integrationId:int}/c/{connectionId:int}")]
    public Task<ActionResult<AttendanceIngestionResult>> ReceiveByConnection(string tenantId, int integrationId, int connectionId, CancellationToken cancellationToken) =>
        ReceiveAsync(tenantId, integrationId, connectionId, cancellationToken);

    private async Task<ActionResult<AttendanceIngestionResult>> ReceiveAsync(string tenantId, int integrationId, int? connectionId, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(cancellationToken);
        if (body is null)
        {
            return Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: $"The body is larger than {MaxBodyBytes} bytes.");
        }

        if (_tenants.Find(tenantId) is not { } tenant)
        {
            return NotAccepted();
        }

        await using var scope = _scopes.CreateScope(tenant.Id);
        var receipt = await scope.ServiceProvider.GetRequiredService<IWebhookReceiver>()
            .ReceiveAsync(
                integrationId,
                connectionId,
                body,
                new WebhookCredentials(Request.Headers[SignatureHeader].ToString(), Request.Headers[ApiKeyHeader].ToString(), Request.Headers.Authorization.ToString()),
                cancellationToken);

        return receipt.Outcome switch
        {
            WebhookOutcome.Accepted => Accepted(receipt.Result),
            WebhookOutcome.Rejected => Problem(statusCode: StatusCodes.Status400BadRequest, title: "The body could not be read.", detail: receipt.Message),
            _ => NotAccepted(),
        };
    }

    private ObjectResult NotAccepted() =>
        Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The webhook request was not accepted.");

    /// <summary>Reads the body, or null when it is larger than <see cref="MaxBodyBytes"/>.</summary>
    private async Task<byte[]?> ReadBodyAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
