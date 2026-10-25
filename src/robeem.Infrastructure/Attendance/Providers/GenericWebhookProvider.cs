using System.Text.Encodings.Web;
using System.Text.Json;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// Reads punches that a system pushed to the integration's webhook. Push only: the Api receives
/// the request, checks its signature and hands the body over as a file, which is read here.
/// There is nothing to pull, so <see cref="GetTransactionChangesAsync"/> throws
/// <see cref="NotSupportedException"/> and the sync job never calls an integration of this kind.
/// </summary>
/// <remarks>
/// <para>The body is JSON: one event object, an array of them, or <c>{"events":[...]}</c>. Each event
/// has <c>transactionId</c>, <c>employeeId</c>, <c>deviceId</c>, <c>timestamp</c>, <c>type</c> and
/// <c>direction</c>; all are optional text (a number is read as its text), and names match ignoring
/// case. The raw payload is an <b>allowlist</b> of exactly those fields: anything else the sender
/// included is never stored (CLAUDE.md §1.10). An event that is not a JSON object makes the whole
/// request unusable, since there is nothing of it that could be stored faithfully.</para>
/// <para>Timestamp and event-type formats are those of <see cref="GenericWebhookInterpreter"/>.</para>
/// </remarks>
public sealed class GenericWebhookProvider : IAttendanceAdapter, IAttendanceMappingGuide
{
    public const string VendorKey = "generic-webhook";

    private const string PushOnly = "The generic webhook is push-only: the sender delivers punches, there is nothing to pull.";

    private static readonly GenericWebhookInterpreter Interpreter = new();

    private static readonly JsonSerializerOptions PayloadOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // The allowlist, with the casing the payload is stored in.
    private static readonly string[] Fields = ["transactionId", "employeeId", "deviceId", "timestamp", "type", "direction"];

    public Task<ConnectionValidationResult> ValidateConnectionAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult(new ConnectionValidationResult(true, "Push provider: there is no connection to validate. The sender signs each request with the integration's secret."));

    public Task<AuthResult> AuthenticateAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult(new AuthResult(true));

    public Task<IReadOnlyList<ExternalDeviceDto>> RetrieveDevicesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ExternalDeviceDto>>([]);

    public Task<IReadOnlyList<ExternalEmployeeDto>> RetrieveEmployeesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ExternalEmployeeDto>>([]);

    /// <summary>Returns every event of the received body, at <see cref="AttendanceIntegrationConfig.UploadFilePath"/>. <paramref name="range"/> is ignored.</summary>
    public async Task<AttendanceBatchResult> RetrieveAttendanceTransactionsAsync(AttendanceIntegrationConfig config, DateRange? range, CancellationToken cancellationToken)
    {
        var path = config.UploadFilePath ?? throw new ArgumentException("The generic webhook adapter reads a received body only; none was given.", nameof(config));

        JsonDocument document;
        try
        {
            await using var stream = File.OpenRead(path);
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The body is not valid JSON.");
        }

        using (document)
        {
            var events = EventsOf(document.RootElement);
            var transactions = new List<ExternalAttendanceTransaction>(events.Count);
            foreach (var element in events)
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("Every event must be a JSON object.");
                }

                transactions.Add(ToTransaction(element));
            }

            return new AttendanceBatchResult(transactions);
        }
    }

    public Task<AttendanceBatchResult> GetTransactionChangesAsync(AttendanceIntegrationConfig config, SyncCursor? cursor, CancellationToken cancellationToken)
        => throw new NotSupportedException(PushOnly);

    public VendorMappingGuide DescribeMapping() => new()
    {
        VendorKey = VendorKey,
        Summary = "Events pushed as JSON to the integration's webhook (one object, an array, or {\"events\": [...]}), each request signed with HMAC-SHA256 of its body under the integration's secret. ISO 8601 timestamps; IN, OUT, BREAK OUT and BREAK IN labels (and their spellings).",
        Fields =
        [
            new FieldMappingRule { Source = "transactionId", Target = "ExternalTransactionId" },
            new FieldMappingRule { Source = "employeeId", Target = "ExternalEmployeeId" },
            new FieldMappingRule { Source = "deviceId", Target = "DeviceExternalId", Note = "Must match a registered device's external id." },
            new FieldMappingRule { Source = "timestamp", Target = "TransactionTimestampRaw", Note = "Without an offset it is wall-clock at the device." },
            new FieldMappingRule { Source = "type", Target = "TransactionTypeRaw", Note = "Tried before direction." },
            new FieldMappingRule { Source = "direction", Target = "DirectionRaw" },
        ],
        TimestampFormats = GenericWebhookInterpreter.TimestampFormats.Select(f => f.Replace("'", string.Empty)).ToList(),
        EventCodes = GenericWebhookInterpreter.RecognizedEventTypes.Select(e => new EventCodeRule { Code = e.Key, EventType = e.Value.ToString() }).ToList(),
        NotStored = ["any field not listed above"],
    };

    private static List<JsonElement> EventsOf(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToList();
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The body must be an event, an array of events, or an object with an events array.");
        }

        var events = root.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, "events", StringComparison.OrdinalIgnoreCase));
        return events.Value.ValueKind == JsonValueKind.Array ? events.Value.EnumerateArray().ToList() : [root];
    }

    private static ExternalAttendanceTransaction ToTransaction(JsonElement element)
    {
        // Only the allowlisted fields are kept, under their canonical names.
        var payload = new Dictionary<string, string>();
        foreach (var property in element.EnumerateObject())
        {
            var field = Fields.FirstOrDefault(f => string.Equals(f, property.Name, StringComparison.OrdinalIgnoreCase));
            var text = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => null,
            };
            if (field is not null && !string.IsNullOrWhiteSpace(text))
            {
                payload[field] = text.Trim();
            }
        }

        string? Field(string name) => payload.TryGetValue(name, out var value) ? value : null;

        var timestampRaw = Field("timestamp") ?? string.Empty;
        return new ExternalAttendanceTransaction(JsonSerializer.Serialize(payload, PayloadOptions), timestampRaw)
        {
            Timestamp = Interpreter.ParseTimestamp(timestampRaw),
            ExternalTransactionId = Field("transactionId"),
            ExternalEmployeeId = Field("employeeId"),
            DeviceExternalId = Field("deviceId"),
            TransactionTypeRaw = Field("type"),
            DirectionRaw = Field("direction"),
        };
    }
}
