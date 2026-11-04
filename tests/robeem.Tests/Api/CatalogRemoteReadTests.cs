using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Controllers;
using robeem.Api.Security;
using robeem.Application;
using robeem.Application.Attendance;
using robeem.Application.Attendance.Catalog;
using robeem.Application.Tenancy;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure;
using robeem.Infrastructure.Attendance.Catalog;
using robeem.Tests.Attendance;

namespace robeem.Tests.Api;

/// <summary>
/// A tenant other than the prime one reads the attendance catalog by calling the prime tenant's Api, not by opening its database
/// (CLAUDE.md §2.8): the endpoints that answer, the key that guards them, the client that asks, and which of the two a scope gets.
/// </summary>
public sealed class CatalogRemoteReadTests : IAsyncDisposable
{
    private const string Key = "a-service-key-that-is-long-enough-to-pass-0123456789";

    private readonly TestDatabase _database = new();
    private readonly TestApi _api;

    public CatalogRemoteReadTests()
    {
        _database.SeedAttendanceCatalog();
        _api = TestApi.Start(services =>
        {
            services.AddScoped(_ => _database.CreateCatalogReader());
            services.Configure<PrimeCatalogOptions>(o => o.ServiceKey = Key);
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        _database.Dispose();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key-wrong-key-wrong-key-wrong-key")]
    public async Task TheCatalogEndpoints_RefuseACallWithoutTheKey_OrWithAWrongOne(string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/catalog/transports");
        if (key is not null)
        {
            request.Headers.Add(CatalogProtocol.ServiceKeyHeader, key);
        }

        using var response = await _api.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnApiWithNoServiceKeyConfigured_RefusesEveryCall_EvenOneThatSendsNoKey()
    {
        await using var unconfigured = TestApi.Start(services => services.AddScoped(_ => _database.CreateCatalogReader()));
        using var response = await unconfigured.Client.GetAsync("api/catalog/transports");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void TheCatalogController_IsAnonymousByDesign_AndGuardedByTheServiceKey_AndOnlyReads()
    {
        Assert.NotNull(typeof(CatalogController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(typeof(CatalogController).GetCustomAttribute<CatalogServiceKeyAttribute>());

        var verbs = typeof(CatalogController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>())
            .SelectMany(a => a.HttpMethods)
            .Distinct();
        Assert.Equal(["GET"], verbs);
    }

    [Fact]
    public async Task TheRemoteReader_GivesWhatTheDatabaseReaderGives()
    {
        var remote = Remote();
        var local = _database.CreateCatalogReader();
        var ct = CancellationToken.None;

        Assert.Equal(Wire(await local.ListLatestFormatsAsync(ct)), Wire(await remote.ListLatestFormatsAsync(ct)));
        Assert.Equal(Wire(await local.ListTransportsAsync(ct)), Wire(await remote.ListTransportsAsync(ct)));
        Assert.Equal(Wire(await local.ListDeviceTypesAsync(ct)), Wire(await remote.ListDeviceTypesAsync(ct)));
        Assert.Equal(Wire(await local.ListConnectorsAsync(ct)), Wire(await remote.ListConnectorsAsync(ct)));

        var zk = (await remote.GetLatestFormatAsync("ZKTeco attendance export", ct))!;
        Assert.NotEmpty(zk.EventCodes);
        Assert.Equal(Wire(await local.GetLatestFormatAsync("ZKTeco attendance export", ct)), Wire(zk));
        Assert.Equal(Wire(await local.GetFormatVersionAsync("ZKTeco attendance export", zk.Version, ct)), Wire(await remote.GetFormatVersionAsync("ZKTeco attendance export", zk.Version, ct)));
        Assert.Equal(zk.Name, (await remote.GetFormatByIdAsync(zk.Id, ct))!.Name);
    }

    [Fact]
    public async Task SeveralKeysAndNames_AreAskedForTogether_AndWhatIsUnknownIsJustMissing()
    {
        var remote = Remote();

        var connectors = await remote.GetConnectorsAsync(["zkteco", "generic-csv", "nope"], CancellationToken.None);
        var formats = await remote.GetLatestFormatsAsync(["ZKTeco attendance export", "No such format"], CancellationToken.None);

        Assert.Equal(["generic-csv", "zkteco"], connectors.Keys.Order());
        Assert.Equal(["ZKTeco attendance export"], formats.Keys);
        Assert.Null(await remote.GetConnectorAsync("no-such-connector", CancellationToken.None));
        Assert.Null(await remote.GetFormatVersionAsync("ZKTeco attendance export", 99, CancellationToken.None));
        Assert.Null(await remote.GetFormatByIdAsync(int.MaxValue, CancellationToken.None));
        Assert.Empty(await remote.GetConnectorsAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task WhetherAConnectorIsUsable_IsDecidedByTheAskingApiOwnAdapters_NotThePrimeApis()
    {
        // The prime Api here knows no adapter at all; this Api has the BioStar one.
        var withAdapter = Remote(new StubAdapterCatalog("suprema-biostar"));
        var without = Remote(new StubAdapterCatalog());

        Assert.True((await withAdapter.GetConnectorAsync("suprema-biostar", CancellationToken.None))!.IsUsable);
        Assert.False((await without.GetConnectorAsync("suprema-biostar", CancellationToken.None))!.IsUsable);
    }

    [Fact]
    public async Task APrimeApiThatAnswersWithAFailure_IsACatalogUnavailable_ThatNeverNamesTheKey()
    {
        var wrongKey = Remote(key: "another-key-another-key-another-key-0000");
        var ex = await Assert.ThrowsAsync<CatalogUnavailableException>(() => wrongKey.ListTransportsAsync(CancellationToken.None));
        Assert.Contains("401", ex.Message);
        Assert.DoesNotContain(Key, ex.ToString());

        var broken = new RemoteCatalogReader(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))) { BaseAddress = new Uri("http://prime.test/api/catalog/") },
            new StubAdapterCatalog(),
            new PayloadDecryptorCatalog([]));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => broken.ListDeviceTypesAsync(CancellationToken.None));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => broken.GetFormatByIdAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task APrimeApiThatCanNotBeReached_OrIsTooSlow_OrSendsGarbage_IsACatalogUnavailable_AndNothingIsServedFromBefore()
    {
        var calls = 0;
        var down = Reader(_ =>
        {
            calls++;
            throw new HttpRequestException("connection refused");
        });
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => down.ListTransportsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => down.ListTransportsAsync(CancellationToken.None));
        Assert.Equal(2, calls);

        var slow = Reader(_ => throw new TaskCanceledException("timed out"));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => slow.ListTransportsAsync(CancellationToken.None));

        var garbage = Reader(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>not json</html>") });
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => garbage.ListTransportsAsync(CancellationToken.None));

        var unknownKind = Reader(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""[{"connectorKey":"x","encryptionKind":"FromTheFuture"}]""") });
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => unknownKind.ListConnectorsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ACallerThatCancels_IsNotMistakenForAnOutage()
    {
        var reader = Reader(_ => throw new TaskCanceledException("cancelled"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ListTransportsAsync(cancelled.Token));
    }

    [Fact]
    public async Task ThePayloadFormatsAndDeviceTypesATenantListsAreReadThroughTheReader_NotTheCatalogDatabase()
    {
        // The catalog database here is empty; only the reader knows the formats. A service that read the database would list nothing.
        using var empty = new TestDatabase();
        using var db = empty.CreateContext(TestDatabase.TenantA);
        var formats = empty.PayloadFormatService(db, TestDatabase.TenantA, catalog: Remote());
        var types = empty.DeviceTypeService(db, TestDatabase.TenantA, catalog: Remote());

        var listed = await formats.ListAsync(CancellationToken.None);
        Assert.Contains(listed, f => f.Name == "ZKTeco attendance export");
        Assert.Contains(await formats.ListOptionsAsync(CancellationToken.None), f => f.Name == "ZKTeco attendance export");
        var input = await formats.GetInputAsync(listed.First(f => f.Name == "ZKTeco attendance export").Id, CancellationToken.None);
        Assert.NotEmpty(input!.EventCodes);
        Assert.NotEmpty(await types.ListOptionsAsync(CancellationToken.None));
    }

    [Fact]
    public void ThePrimeTenantsScopeReadsTheDatabase_AnyOtherScopeAsksThePrimeApi()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenancy:PrimeTenantId"] = "prime",
            ["Tenancy:Tenants:0:Id"] = "prime",
            ["Tenancy:Tenants:0:Name"] = "Prime",
            ["Tenancy:Tenants:0:ConnectionString"] = "Host=localhost;Database=robeem_prime;Username=postgres",
            ["Tenancy:Tenants:1:Id"] = "demo",
            ["Tenancy:Tenants:1:Name"] = "Demo",
            ["Tenancy:Tenants:1:ConnectionString"] = "Host=localhost;Database=robeem_demo;Username=postgres",
            ["Catalog:PrimeApiBaseUrl"] = "https://prime.example.test",
            ["Catalog:ServiceKey"] = Key,
        }).Build();
        using var services = new ServiceCollection().AddLogging().AddRobeemInfrastructure(config).AddRobeemApplication().BuildServiceProvider();
        var scopes = services.GetRequiredService<ITenantScopeFactory>();

        using (var prime = scopes.CreateScope("prime"))
        {
            Assert.IsType<CatalogReader>(prime.ServiceProvider.GetRequiredService<ICatalogReader>());
        }

        using (var demo = scopes.CreateScope("demo"))
        {
            Assert.IsType<RemoteCatalogReader>(demo.ServiceProvider.GetRequiredService<ICatalogReader>());
        }
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("https://prime.example.test", "short", false)]
    [InlineData("not a url", Key, false)]
    [InlineData("https://prime.example.test", Key, true)]
    public void TheOptions_NeedAnAddressAndALongEnoughKey(string? url, string? key, bool valid)
    {
        var options = new PrimeCatalogOptions { PrimeApiBaseUrl = url, ServiceKey = key };

        Assert.Equal(valid, options.Validate(requireHttps: true).Count == 0);
    }

    [Fact]
    public void OutsideDevelopment_TheKeyMayOnlyTravelOverHttps()
    {
        var options = new PrimeCatalogOptions { PrimeApiBaseUrl = "http://prime.example.test", ServiceKey = Key };

        Assert.Empty(options.Validate(requireHttps: false));
        Assert.Single(options.Validate(requireHttps: true));
    }

    private static string Wire(IEnumerable<AttendancePayloadFormat> items) => JsonSerializer.Serialize(items.Select(CatalogWireMapper.ToWire));

    private static string Wire(IEnumerable<AttendanceTransportDefinition> items) => JsonSerializer.Serialize(items.Select(CatalogWireMapper.ToWire));

    private static string Wire(IEnumerable<AttendanceDeviceType> items) => JsonSerializer.Serialize(items.Select(CatalogWireMapper.ToWire));

    private static string Wire(IEnumerable<ConnectorInfo> items) => JsonSerializer.Serialize(items.Select(CatalogWireMapper.ToWire));

    private static string Wire(AttendancePayloadFormat? item) => JsonSerializer.Serialize(item is null ? null : CatalogWireMapper.ToWire(item));

    private static RemoteCatalogReader Reader(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("http://prime.test/api/catalog/") }, new StubAdapterCatalog(), new PayloadDecryptorCatalog([]));

    /// <summary>A reader whose calls go to the in-memory prime Api, with the service key (the right one unless told otherwise).</summary>
    private RemoteCatalogReader Remote(IAttendanceAdapterCatalog? adapters = null, string key = Key)
    {
        var http = _api.CreateClient();
        http.BaseAddress = new Uri(http.BaseAddress!, CatalogProtocol.Route + "/");
        http.DefaultRequestHeaders.Add(CatalogProtocol.ServiceKeyHeader, key);
        return new RemoteCatalogReader(http, adapters ?? new StubAdapterCatalog(), new PayloadDecryptorCatalog([]));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_respond(request));
        }
    }
}
