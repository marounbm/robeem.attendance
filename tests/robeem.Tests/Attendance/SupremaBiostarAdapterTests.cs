using System.Net;
using System.Text;
using System.Text.Json;
using robeem.Application.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;

namespace robeem.Tests.Attendance;

public sealed class SupremaBiostarAdapterTests
{
    private const string Password = "s3cret-pw";

    private const string Secret = $$"""{"baseUrl":"https://biostar.test:3002","loginId":"admin","password":"{{Password}}"}""";

    private static readonly DateTime Now = new(2026, 4, 10, 12, 0, 0);

    private readonly FakeBiostar _server = new();

    [Fact]
    public async Task ASync_LogsIn_SearchesEventsWithTheSessionAndAnOrderedWindow_AndLogsOut()
    {
        _server.Pages.Enqueue(Events(Row(1, "2026-04-01T05:33:25.00Z", userId: "21001", deviceId: "541", tnaKey: "1")));

        await Provider().GetTransactionChangesAsync(Config(), null, CancellationToken.None);

        Assert.Equal(["POST /api/login", "POST /api/events/search", "POST /api/logout"], _server.Calls.Select(c => $"{c.Method} {c.Path}"));
        Assert.Equal("biostar.test", _server.Calls[0].Host);
        Assert.Contains("\"login_id\":\"admin\"", _server.Calls[0].Body);
        Assert.Null(_server.Calls[0].Session);
        Assert.Equal("session-1", _server.Calls[1].Session);
        Assert.Equal("session-1", _server.Calls[2].Session);

        var query = JsonDocument.Parse(_server.Calls[1].Body!).RootElement.GetProperty("Query");
        Assert.Equal(1000, query.GetProperty("limit").GetInt32());
        var condition = query.GetProperty("conditions")[0];
        Assert.Equal(("datetime", 3), (condition.GetProperty("column").GetString(), condition.GetProperty("operator").GetInt32()));
        Assert.False(query.GetProperty("orders")[0].GetProperty("descending").GetBoolean());
    }

    [Fact]
    public async Task WithNoCursor_TheFirstSyncReadsTheLastSevenDays()
    {
        _server.Pages.Enqueue(Events());

        await Provider().GetTransactionChangesAsync(Config(), null, CancellationToken.None);

        var values = Window(_server.Calls[1]);
        Assert.Equal("2026-04-03T12:00:00.000Z", values.From);

        // Up to now plus a margin for a device whose clock runs ahead.
        Assert.Equal("2026-04-10T12:05:00.000Z", values.To);
    }

    [Fact]
    public async Task WithACursor_TheSyncStartsBeforeItByTheOverlap_AndTheCursorMovesToTheLatestEvent()
    {
        _server.Pages.Enqueue(Events(
            Row(1, "2026-04-09T08:00:00.00Z", userId: "21001", deviceId: "541", tnaKey: "1"),
            Row(2, "2026-04-09T16:30:15.00Z", userId: "21001", deviceId: "541", tnaKey: "2")));
        var config = Config() with { SyncOverlap = TimeSpan.FromMinutes(10) };

        var result = await Provider().GetTransactionChangesAsync(config, new SyncCursor("2026-04-09T07:00:00.0000000Z"), CancellationToken.None);

        Assert.Equal("2026-04-09T06:50:00.000Z", Window(_server.Calls[1]).From);
        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal("2026-04-09T16:30:15.0000000Z", result.NextCursor!.Value);
    }

    [Fact]
    public async Task ASyncThatFindsNothing_LeavesTheCursorWhereItWas()
    {
        _server.Pages.Enqueue(Events());

        var result = await Provider().GetTransactionChangesAsync(Config(), new SyncCursor("2026-04-09T07:00:00.0000000Z"), CancellationToken.None);

        Assert.Empty(result.Transactions);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task AReconciliationWindow_IsSearchedAsGiven_AndNeverReturnsACursor()
    {
        _server.Pages.Enqueue(Events(Row(1, "2026-04-09T08:00:00.00Z", userId: "21001", deviceId: "541", tnaKey: "1")));
        var range = new DateRange(new DateTimeOffset(2026, 4, 8, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero));

        var result = await Provider().RetrieveAttendanceTransactionsAsync(Config(), range, CancellationToken.None);

        Assert.Equal("2026-04-08T00:00:00.000Z", Window(_server.Calls[1]).From);
        Assert.Single(result.Transactions);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task AnEvent_MapsItsFields_AndKeepsOnlyTheAllowlistInThePayload()
    {
        _server.Pages.Enqueue(Events("""
            {"id":"9001","datetime":"2026-04-01T05:33:25.00Z","tna_key":"1",
             "user_id":{"user_id":"21001","name":"Hiba Rahme","photo_exists":true,"fingerprint_template":"AAEC"},
             "device_id":{"id":"541","name":"Front door"},
             "event_type_id":{"code":"4865"},
             "card_id":"0099887","door_id":[{"id":"7"}]}
            """));

        var tx = (await Provider().GetTransactionChangesAsync(Config(), null, CancellationToken.None)).Transactions.Single();

        Assert.Equal(("9001", "21001", "541", "1", "2026-04-01T05:33:25.00Z"), (tx.ExternalTransactionId, tx.ExternalEmployeeId, tx.DeviceExternalId, tx.TransactionTypeRaw, tx.TransactionTimestampRaw));
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 5, 33, 25, TimeSpan.Zero), tx.Timestamp!.Value.ToUtc(null));

        var names = JsonDocument.Parse(tx.RawPayload).RootElement.EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(["datetime", "device_id", "event_type_code", "id", "tna_key", "user_id"], names);
        Assert.DoesNotContain("Hiba", tx.RawPayload);
        Assert.DoesNotContain("AAEC", tx.RawPayload);
        Assert.DoesNotContain("0099887", tx.RawPayload);
    }

    [Fact]
    public async Task AnEventWithoutATnaKey_IsKeptAsAnUnlabeledPunch_NotDropped()
    {
        _server.Pages.Enqueue(Events(
            Row(1, "2026-04-01T05:33:25.00Z", userId: "21001", deviceId: "541", tnaKey: "0"),
            Row(2, "2026-04-01T05:34:25.00Z", userId: "21001", deviceId: "541", tnaKey: null)));

        var result = await Provider().GetTransactionChangesAsync(Config(), null, CancellationToken.None);

        Assert.Equal(2, result.Transactions.Count);
        Assert.All(result.Transactions, t => Assert.Null(t.TransactionTypeRaw));
    }

    [Fact]
    public async Task AFullPage_IsFollowedByTheNext_AndAnEventSeenTwiceIsKeptOnce()
    {
        var first = Enumerable.Range(1, 1000).Select(i => Row(i, new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.Zero).AddSeconds(i).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.00'Z'"), "21001", "541", "1"));
        _server.Pages.Enqueue(Events(first.ToArray()));

        // The next window starts at the last instant, so that event is sent again, then one new one.
        _server.Pages.Enqueue(Events(
            Row(1000, "2026-04-09T00:16:40.00Z", "21001", "541", "1"),
            Row(1001, "2026-04-09T00:16:41.00Z", "21001", "541", "2")));

        var result = await Provider().GetTransactionChangesAsync(Config(), null, CancellationToken.None);

        Assert.Equal(1001, result.Transactions.Count);
        Assert.Equal(1001, result.Transactions.Select(t => t.ExternalTransactionId).Distinct().Count());
        Assert.Equal(2, _server.Calls.Count(c => c.Path == "/api/events/search"));
        Assert.Equal("2026-04-09T00:16:40.000Z", Window(_server.Calls.Where(c => c.Path == "/api/events/search").ElementAt(1)).From);
        Assert.StartsWith("2026-04-09T00:16:41", result.NextCursor!.Value);
    }

    [Fact]
    public async Task APageOfEventsAtOneInstant_DoesNotLoopForever()
    {
        // 1000 events at the same second, and the server keeps answering with the same page.
        var page = Events(Enumerable.Range(1, 1000).Select(i => Row(i, "2026-04-09T00:00:01.00Z", "21001", "541", "1")).ToArray());
        _server.Pages.Enqueue(page);
        _server.Pages.Enqueue(page);
        _server.Pages.Enqueue(Events());

        var result = await Provider().GetTransactionChangesAsync(Config(), null, CancellationToken.None);

        Assert.Equal(1000, result.Transactions.Count);
        Assert.True(_server.Calls.Count(c => c.Path == "/api/events/search") <= 3);
    }

    [Fact]
    public async Task ARefusedLogin_FailsWithTheStatus_NeverThePassword_AndSearchesNothing()
    {
        _server.LoginStatus = HttpStatusCode.Unauthorized;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Provider().GetTransactionChangesAsync(Config(), null, CancellationToken.None));

        Assert.Contains("401", ex.Message);
        Assert.DoesNotContain(Password, ex.Message);
        Assert.DoesNotContain(_server.Calls, c => c.Path == "/api/events/search");
    }

    [Fact]
    public async Task AFailedSearch_StillLogsOut()
    {
        _server.SearchStatus = HttpStatusCode.InternalServerError;

        await Assert.ThrowsAsync<HttpRequestException>(() => Provider().GetTransactionChangesAsync(Config(), null, CancellationToken.None));

        Assert.Equal("/api/logout", _server.Calls[^1].Path);
    }

    [Fact]
    public async Task AnAnswerWithoutAnEventList_IsAnErrorNotAnEmptySync()
    {
        _server.Pages.Enqueue("""{"Response":{"code":"1","message":"nope"}}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().GetTransactionChangesAsync(Config(), new SyncCursor("2026-04-09T07:00:00.0000000Z"), CancellationToken.None));

        Assert.Contains("no EventCollection list", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"baseUrl":"http://plain.test","loginId":"a","password":"b"}""")]
    [InlineData("""{"baseUrl":"https://biostar.test","loginId":"a"}""")]
    public async Task ABadSecret_IsRefused_WithoutRepeatingIt(string? secret)
    {
        var config = Config() with { ConnectionSecret = secret };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().GetTransactionChangesAsync(config, null, CancellationToken.None));

        Assert.Contains("must be JSON with baseUrl (https), loginId and password", ex.Message);
        if (!string.IsNullOrEmpty(secret))
        {
            Assert.DoesNotContain(secret, ex.Message);
        }

        Assert.Empty(_server.Calls);
    }

    [Fact]
    public async Task TheSelfSignedClient_IsUsedOnlyWhenTheSecretAsksForIt()
    {
        _server.Pages.Enqueue(Events());
        _server.Pages.Enqueue(Events());
        var untrusted = Config() with { ConnectionSecret = """{"baseUrl":"https://biostar.test","loginId":"a","password":"b","allowUntrustedCertificate":true}""" };

        await Provider().GetTransactionChangesAsync(Config(), null, CancellationToken.None);
        await Provider().GetTransactionChangesAsync(untrusted, null, CancellationToken.None);

        Assert.Equal([SupremaBiostarProvider.HttpClientName, SupremaBiostarProvider.UntrustedHttpClientName], _server.ClientNamesUsed.Distinct());
        Assert.Equal(SupremaBiostarProvider.HttpClientName, _server.ClientNamesUsed.First());
    }

    [Fact]
    public async Task ValidatingTheConnection_ReportsALoginFailureAsAnAnswer_NotAnException()
    {
        _server.LoginStatus = HttpStatusCode.Forbidden;

        var bad = await Provider().ValidateConnectionAsync(Config(), CancellationToken.None);
        var missing = await Provider().ValidateConnectionAsync(Config() with { ConnectionSecret = null }, CancellationToken.None);

        Assert.False(bad.IsValid);
        Assert.Contains("403", bad.Message);
        Assert.False(missing.IsValid);

        _server.LoginStatus = HttpStatusCode.OK;
        Assert.True((await Provider().ValidateConnectionAsync(Config(), CancellationToken.None)).IsValid);
    }

    [Fact]
    public async Task Devices_AreReadFromTheServer()
    {
        _server.Devices = """{"DeviceCollection":{"rows":[{"id":"541","name":"Front door","device_type_id":{"name":"BioStation 2"}},{"id":"542","name":"Back door"}]}}""";

        var devices = await Provider().RetrieveDevicesAsync(Config(), CancellationToken.None);

        Assert.Equal([("541", "Front door", "BioStation 2"), ("542", "Back door", null)], devices.Select(d => (d.ExternalDeviceId, d.Name, d.DeviceType)));
        Assert.All(devices, d => Assert.Null(d.TimeZoneId));
    }

    [Fact]
    public void TheSettingsRecord_NeverPrintsThePassword() =>
        Assert.DoesNotContain(Password, BiostarConnectionSettings.Parse(Secret).ToString());

    [Theory]
    [InlineData("2026-04-01T05:33:25.00Z", 5, 0)]
    [InlineData("2026-04-01T05:33:25Z", 5, 0)]
    [InlineData("2026-04-01T08:33:25+03:00", 8, 3)]
    public void Interpreter_ReadsIsoInstants_KeepingTheOffset(string text, int hour, int offsetHours)
    {
        var parsed = new SupremaBiostarInterpreter().ParseTimestamp(text);

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(2026, 4, 1, hour, 33, 25), parsed.Value.WallClock);
        Assert.Equal(TimeSpan.FromHours(offsetHours), parsed.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 5, 33, 25, TimeSpan.Zero), parsed.Value.ToUtc(null));
    }

    [Fact]
    public void Interpreter_ReadsATimeWithoutAnOffsetAsDeviceWallClock_AndNothingElse()
    {
        var interpreter = new SupremaBiostarInterpreter();

        Assert.Null(interpreter.ParseTimestamp("2026-04-01T05:33:25")!.Value.Offset);
        Assert.Null(interpreter.ParseTimestamp("01/04/2026 05:33"));
        Assert.Null(interpreter.ParseTimestamp(string.Empty));
    }

    [Theory]
    [InlineData("1", NormalizedEventType.CheckIn)]
    [InlineData("2", NormalizedEventType.CheckOut)]
    [InlineData("3", NormalizedEventType.BreakOut)]
    [InlineData("4", NormalizedEventType.BreakIn)]
    [InlineData("5", NormalizedEventType.BreakOut)]
    [InlineData("6", NormalizedEventType.BreakIn)]
    public void Interpreter_MapsTheTnaKeys(string key, NormalizedEventType expected) =>
        Assert.Equal(expected, new SupremaBiostarInterpreter().MapEventType(key, null));

    [Theory]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("check in")]
    public void Interpreter_DoesNotGuessAKeyItDoesNotKnow(string key) =>
        Assert.Null(new SupremaBiostarInterpreter().MapEventType(key, null));

    [Fact]
    public void Guide_ListsTheTnaKeys_AndWhatIsNeverStored()
    {
        var guide = Provider().DescribeMapping();

        Assert.Equal(SupremaBiostarProvider.ProviderKey, guide.ProviderKey);
        Assert.Equal(SupremaBiostarInterpreter.EventCodes.Count, guide.EventCodes.Count);
        Assert.Contains(guide.EventCodes, c => c.Code.Contains("PUNCH_TYPE_CHECK_IN") && c.EventType == nameof(NormalizedEventType.CheckIn));
        Assert.Contains(guide.NotStored, n => n.Contains("user record"));
    }

    private static string Row(int id, string datetime, string? userId, string? deviceId, string? tnaKey)
    {
        var tna = tnaKey is null ? string.Empty : $",\"tna_key\":\"{tnaKey}\"";
        return "{\"id\":\"" + id + "\",\"datetime\":\"" + datetime + "\"" + tna
            + ",\"user_id\":{\"user_id\":\"" + userId + "\"},\"device_id\":{\"id\":\"" + deviceId + "\"},\"event_type_id\":{\"code\":\"4865\"}}";
    }

    private static string Events(params string[] rows) =>
        "{\"EventCollection\":{\"rows\":[" + string.Join(",", rows) + "]},\"Response\":{\"code\":\"0\"}}";

    private static (string From, string To) Window(FakeBiostar.Call search)
    {
        var values = JsonDocument.Parse(search.Body!).RootElement.GetProperty("Query").GetProperty("conditions")[0].GetProperty("values");
        return (values[0].GetString()!, values[1].GetString()!);
    }

    private static AttendanceIntegrationConfig Config() =>
        new(1, SupremaBiostarProvider.ProviderKey, "biostar-hq") { ConnectionSecret = Secret };

    private SupremaBiostarProvider Provider() => new(_server, new FixedTimeProvider(Now));

    /// <summary>A scripted BioStar server: a client factory whose clients answer from the queued pages.</summary>
    private sealed class FakeBiostar : IHttpClientFactory
    {
        private int _sessions;

        public Queue<string> Pages { get; } = new();

        public List<Call> Calls { get; } = [];

        public List<string> ClientNamesUsed { get; } = [];

        public HttpStatusCode LoginStatus { get; set; } = HttpStatusCode.OK;

        public HttpStatusCode SearchStatus { get; set; } = HttpStatusCode.OK;

        public string Devices { get; set; } = """{"DeviceCollection":{"rows":[]}}""";

        public HttpClient CreateClient(string name)
        {
            ClientNamesUsed.Add(name);
            return new HttpClient(new Handler(this));
        }

        public sealed record Call(string Method, string Host, string Path, string? Body, string? Session);

        private sealed class Handler : HttpMessageHandler
        {
            private readonly FakeBiostar _owner;

            public Handler(FakeBiostar owner)
            {
                _owner = owner;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                var session = request.Headers.TryGetValues("bs-session-id", out var values) ? values.First() : null;
                var path = request.RequestUri!.AbsolutePath;
                _owner.Calls.Add(new Call(request.Method.Method, request.RequestUri.Host, path, body, session));

                switch (path)
                {
                    case "/api/login":
                        var login = new HttpResponseMessage(_owner.LoginStatus) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                        if (_owner.LoginStatus == HttpStatusCode.OK)
                        {
                            login.Headers.Add("bs-session-id", $"session-{++_owner._sessions}");
                        }

                        return login;
                    case "/api/events/search":
                        return _owner.SearchStatus != HttpStatusCode.OK
                            ? new HttpResponseMessage(_owner.SearchStatus)
                            : Json(_owner.Pages.Count > 0 ? _owner.Pages.Dequeue() : Events());
                    case "/api/devices":
                        return Json(_owner.Devices);
                    default:
                        return new HttpResponseMessage(HttpStatusCode.OK);
                }
            }

            private static HttpResponseMessage Json(string json) =>
                new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
