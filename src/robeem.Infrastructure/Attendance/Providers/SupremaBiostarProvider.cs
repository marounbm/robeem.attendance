using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// Pulls events from a Suprema BioStar 2 server over its Local REST API. The connection is the
/// integration's secret (<see cref="BiostarConnectionSettings"/>), which the sync job resolves for
/// the length of one call and which is never logged or stored.
/// </summary>
/// <remarks>
/// <para>A call logs in (<c>POST /api/login</c>, the session id comes back in the
/// <c>bs-session-id</c> header), searches events (<c>POST /api/events/search</c>, a UTC
/// <c>datetime</c> window ordered by time, one page of <see cref="PageSize"/> at a time) and logs
/// out. Retry and the circuit breaker are the sync service's, around the whole call.</para>
/// <para>The cursor is the latest event time seen, as a UTC ISO 8601 instant. An incremental sync
/// starts <see cref="AttendanceIntegrationConfig.SyncOverlap"/> before it, or
/// <see cref="InitialLookback"/> before now when there is no cursor yet; the events already stored
/// come back and duplicate detection absorbs them (CLAUDE.md §1.4).</para>
/// <para>The payload is an <b>allowlist</b>: id, datetime, device id, user id, T&amp;A key and event
/// type code. Nothing else of the event is kept, and the BioStar user record (which can carry
/// credentials) is never read (CLAUDE.md §1.10). An event without a T&amp;A key is kept as an
/// unlabeled punch, because the API cannot filter on it and a door event is not for the adapter to
/// drop (CLAUDE.md §1.1).</para>
/// </remarks>
public sealed class SupremaBiostarProvider : IAttendanceAdapter, IAttendanceMappingGuide
{
    public const string VendorKey = "suprema-biostar";

    /// <summary>The name of the <see cref="IHttpClientFactory"/> client that verifies the server's certificate.</summary>
    public const string HttpClientName = "suprema-biostar";

    /// <summary>The name of the client for a server with a self-signed certificate, chosen only by <c>allowUntrustedCertificate</c> in the secret.</summary>
    public const string UntrustedHttpClientName = "suprema-biostar-untrusted";

    /// <summary>The most events one search asks for.</summary>
    internal const int PageSize = 1000;

    private const string SessionHeader = "bs-session-id";

    private static readonly SupremaBiostarInterpreter Interpreter = new();

    private static readonly TimeSpan FutureMargin = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _http;
    private readonly TimeProvider _clock;

    public SupremaBiostarProvider(IHttpClientFactory http, TimeProvider clock)
    {
        _http = http;
        _clock = clock;
    }

    /// <summary>Gets how far back the first incremental sync, and a search with no range, reads.</summary>
    public static TimeSpan InitialLookback { get; } = TimeSpan.FromDays(7);

    public async Task<ConnectionValidationResult> ValidateConnectionAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
    {
        try
        {
            var settings = BiostarConnectionSettings.Parse(config.ConnectionSecret, config.Settings.GetValueOrDefault("baseUrl"));
            await LogoutQuietlyAsync(settings, await LoginAsync(settings, cancellationToken), cancellationToken);
            return new ConnectionValidationResult(true, "Logged in to BioStar.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            return new ConnectionValidationResult(false, ex.Message);
        }
    }

    public async Task<AuthResult> AuthenticateAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
    {
        try
        {
            var settings = BiostarConnectionSettings.Parse(config.ConnectionSecret, config.Settings.GetValueOrDefault("baseUrl"));
            return new AuthResult(true, await LoginAsync(settings, cancellationToken));
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            return new AuthResult(false, Error: ex.Message);
        }
    }

    public async Task<IReadOnlyList<ExternalDeviceDto>> RetrieveDevicesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
    {
        var settings = BiostarConnectionSettings.Parse(config.ConnectionSecret, config.Settings.GetValueOrDefault("baseUrl"));
        var session = await LoginAsync(settings, cancellationToken);
        try
        {
            using var request = Request(HttpMethod.Get, settings, "api/devices", session);
            using var response = await Client(settings).SendAsync(request, cancellationToken);
            using var document = await ReadAsync(response, "api/devices", cancellationToken);

            var devices = new List<ExternalDeviceDto>();
            foreach (var row in Rows(document.RootElement, "DeviceCollection"))
            {
                if (Scalar(row, "id") is { } id)
                {
                    devices.Add(new ExternalDeviceDto(id, Scalar(row, "name"), Scalar(row, "device_type_id", "name"), null));
                }
            }

            return devices;
        }
        finally
        {
            await LogoutQuietlyAsync(settings, session, cancellationToken);
        }
    }

    /// <summary>The employee list is not read: the BioStar user record can hold credentials, so vendor ids are mapped by hand.</summary>
    public Task<IReadOnlyList<ExternalEmployeeDto>> RetrieveEmployeesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ExternalEmployeeDto>>([]);

    public async Task<AttendanceBatchResult> RetrieveAttendanceTransactionsAsync(AttendanceIntegrationConfig config, DateRange? range, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var window = range ?? new DateRange(now - InitialLookback, now);
        var (transactions, _) = await SearchAsync(config, window.From, window.To, cancellationToken);

        // A fixed window (reconciliation) never moves the cursor.
        return new AttendanceBatchResult(transactions);
    }

    public async Task<AttendanceBatchResult> GetTransactionChangesAsync(AttendanceIntegrationConfig config, SyncCursor? cursor, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var from = cursor is not null && DateTimeOffset.TryParse(cursor.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)
            ? last - (config.SyncOverlap ?? TimeSpan.Zero)
            : now - InitialLookback;

        var (transactions, latest) = await SearchAsync(config, from, now, cancellationToken);
        return new AttendanceBatchResult(transactions, latest is { } at ? new SyncCursor(at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)) : null);
    }

    public VendorMappingGuide DescribeMapping() => new()
    {
        VendorKey = VendorKey,
        Summary = "Pulled from the BioStar 2 Local API (events search) with the connection secret. Event times are UTC; the device's time zone does not change them. Events without a T&A key are stored as unlabeled punches, and the tenant's attendance policy decides what happens to them.",
        Fields =
        [
            new FieldMappingRule { Source = "id", Target = "ExternalTransactionId" },
            new FieldMappingRule { Source = "user_id.user_id", Target = "ExternalEmployeeId", Note = "Map it to an employee under the integration's employee ids." },
            new FieldMappingRule { Source = "device_id.id", Target = "DeviceExternalId", Note = "Must match a registered device's external id." },
            new FieldMappingRule { Source = "datetime", Target = "TransactionTimestampRaw", Note = "ISO 8601 UTC." },
            new FieldMappingRule { Source = "tna_key", Target = "TransactionTypeRaw", Note = "Absent or 0: no type." },
            new FieldMappingRule { Source = "event_type_id.code", Target = "payload only" },
        ],
        TimestampFormats = SupremaBiostarInterpreter.TimestampFormats,
        EventCodes = SupremaBiostarInterpreter.EventCodes.Select(c => new EventCodeRule { Code = $"tna_key {c.Key} ({c.Name})", EventType = c.Type.ToString() }).ToList(),
        NotStored = ["the user record (name, credentials, photo)", "any event field not listed above"],
    };

    private static string? Scalar(JsonElement parent, string name, string? inner = null)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return inner is null ? null : Scalar(value, inner);
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static IEnumerable<JsonElement> Rows(JsonElement root, string collection)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(collection, out var wrapper)
            && wrapper.ValueKind == JsonValueKind.Object
            && wrapper.TryGetProperty("rows", out var rows)
            && rows.ValueKind == JsonValueKind.Array)
        {
            return rows.EnumerateArray();
        }

        throw new InvalidOperationException($"BioStar's answer had no {collection} list.");
    }

    private static Uri Endpoint(BiostarConnectionSettings settings, string path) =>
        new(new Uri(settings.BaseUrl.AbsoluteUri.TrimEnd('/') + "/"), path);

    private static HttpRequestMessage Request(HttpMethod method, BiostarConnectionSettings settings, string path, string? session)
    {
        var request = new HttpRequestMessage(method, Endpoint(settings, path));
        if (session is not null)
        {
            request.Headers.Add(SessionHeader, session);
        }

        return request;
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        // The status only: the body can echo what was sent.
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"BioStar answered HTTP {(int)response.StatusCode} for {path}.");
        }

        try
        {
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"BioStar's answer for {path} was not JSON.");
        }
    }

    private static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static (string Id, DateTimeOffset? At, ExternalAttendanceTransaction Transaction) ToTransaction(JsonElement row)
    {
        var id = Scalar(row, "id");
        var timestamp = Scalar(row, "datetime") ?? string.Empty;
        var employee = Scalar(row, "user_id", "user_id");
        var device = Scalar(row, "device_id", "id");
        var tnaKey = Scalar(row, "tna_key");
        if (tnaKey == "0")
        {
            tnaKey = null;
        }

        // Only these fields are kept (CLAUDE.md §1.10).
        var payload = new Dictionary<string, string>();
        void Keep(string name, string? value)
        {
            if (value is not null)
            {
                payload[name] = value;
            }
        }

        Keep("id", id);
        Keep("datetime", timestamp.Length == 0 ? null : timestamp);
        Keep("device_id", device);
        Keep("user_id", employee);
        Keep("tna_key", tnaKey);
        Keep("event_type_code", Scalar(row, "event_type_id", "code"));

        var vendorTime = Interpreter.ParseTimestamp(timestamp);
        var transaction = new ExternalAttendanceTransaction(JsonSerializer.Serialize(payload), timestamp)
        {
            Timestamp = vendorTime,
            ExternalTransactionId = id,
            ExternalEmployeeId = employee,
            DeviceExternalId = device,
            TransactionTypeRaw = tnaKey,
        };

        // Within a search an event is told apart by its id; one without an id by what it says.
        return (id ?? $"{timestamp}|{employee}|{device}|{tnaKey}", vendorTime?.ToUtc(null), transaction);
    }

    private HttpClient Client(BiostarConnectionSettings settings) =>
        _http.CreateClient(settings.AllowUntrustedCertificate ? UntrustedHttpClientName : HttpClientName);

    private async Task<string> LoginAsync(BiostarConnectionSettings settings, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, settings, "api/login", null);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { User = new { login_id = settings.LoginId, password = settings.Password } }),
            Encoding.UTF8,
            new MediaTypeHeaderValue("application/json"));

        using var response = await Client(settings).SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"BioStar refused the login (HTTP {(int)response.StatusCode}).");
        }

        return response.Headers.TryGetValues(SessionHeader, out var values) && values.FirstOrDefault() is { Length: > 0 } session
            ? session
            : throw new InvalidOperationException("BioStar accepted the login but sent no session id.");
    }

    private async Task LogoutQuietlyAsync(BiostarConnectionSettings settings, string session, CancellationToken cancellationToken)
    {
        // Frees the session on the server. A failure here changes nothing about the data just read.
        try
        {
            using var request = Request(HttpMethod.Post, settings, "api/logout", session);
            using var response = await Client(settings).SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
        }
    }

    private async Task<(List<ExternalAttendanceTransaction> Transactions, DateTimeOffset? Latest)> SearchAsync(AttendanceIntegrationConfig config, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var settings = BiostarConnectionSettings.Parse(config.ConnectionSecret, config.Settings.GetValueOrDefault("baseUrl"));
        var session = await LoginAsync(settings, cancellationToken);
        try
        {
            var upTo = to + FutureMargin;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var transactions = new List<ExternalAttendanceTransaction>();
            DateTimeOffset? latest = null;
            var cursor = from;

            while (true)
            {
                var page = await SearchPageAsync(settings, session, cursor, upTo, cancellationToken);
                var fresh = page.Where(e => seen.Add(e.Id)).ToList();
                transactions.AddRange(fresh.Select(e => e.Transaction));

                var last = page.Where(e => e.At is not null).Select(e => e.At!.Value).DefaultIfEmpty().Max();
                if (last != default && (latest is null || last > latest))
                {
                    latest = last;
                }

                if (page.Count < PageSize || last == default)
                {
                    break;
                }

                // A full page: continue from its last instant (the events at it come back and are skipped by id).
                // When nothing in it was new, a whole page shares one instant: step past it, or the search would never end.
                cursor = fresh.Count == 0 ? last.AddMilliseconds(1) : last;
                if (cursor >= upTo)
                {
                    break;
                }
            }

            return (transactions, latest);
        }
        finally
        {
            await LogoutQuietlyAsync(settings, session, cancellationToken);
        }
    }

    private async Task<List<(string Id, DateTimeOffset? At, ExternalAttendanceTransaction Transaction)>> SearchPageAsync(BiostarConnectionSettings settings, string session, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var body = new
        {
            Query = new
            {
                limit = PageSize,
                conditions = new[] { new { column = "datetime", @operator = 3, values = new[] { Instant(from), Instant(to) } } },
                orders = new[] { new { column = "datetime", descending = false } },
            },
        };

        using var request = Request(HttpMethod.Post, settings, "api/events/search", session);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

        using var response = await Client(settings).SendAsync(request, cancellationToken);
        using var document = await ReadAsync(response, "api/events/search", cancellationToken);
        return Rows(document.RootElement, "EventCollection").Select(ToTransaction).ToList();
    }
}
