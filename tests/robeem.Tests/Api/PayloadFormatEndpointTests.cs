using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Tests.Api;

/// <summary>
/// The payload formats are the prime tenant's offering to every tenant: any tenant's integration administrator reads them and downloads an
/// example file, and only a platform administrator creates or changes one.
/// </summary>
public sealed class PayloadFormatEndpointTests : IAsyncDisposable
{
    private static readonly string Tenant = TestApi.As(5, AttendanceRoles.IntegrationAdministrator);
    private static readonly string Platform = TestApi.As(1, AttendanceRoles.PlatformAdministrator);

    private readonly FakeFormats _formats = new();
    private readonly TestApi _api;

    public PayloadFormatEndpointTests()
    {
        _api = TestApi.Start(services => services.AddSingleton<IAttendancePayloadFormatService>(_formats));
    }

    public static IEnumerable<object[]> Reads() =>
    [
        ["GET", "api/attendance/payload-formats", null!],
        ["GET", "api/attendance/payload-formats/options", null!],
        ["GET", "api/attendance/payload-formats/1/input", null!],
        ["GET", "api/attendance/payload-formats/1/sample?kind=csv", null!],
    ];

    public static IEnumerable<object[]> Writes() =>
    [
        ["POST", "api/attendance/payload-formats", """{"name":"Mine"}"""],
        ["PUT", "api/attendance/payload-formats/1", """{"name":"Mine"}"""],
        ["POST", "api/attendance/payload-formats/sample?kind=csv", """{"name":"Mine"}"""],
    ];

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task EveryTenantsIntegrationAdministrator_ReadsTheFormats_AndSoDoesAPlatformAdministrator(string method, string url, string? body)
    {
        foreach (var role in new[] { AttendanceRoles.Employee, AttendanceRoles.Manager, AttendanceRoles.HRAdministrator, AttendanceRoles.PayrollAdministrator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(method, url, TestApi.As(7, role), body)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(method, url, null, body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(method, url, Tenant, body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(method, url, Platform, body)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task OnlyAPlatformAdministrator_CreatesOrChangesAFormat(string method, string url, string? body)
    {
        foreach (var role in new[] { AttendanceRoles.Employee, AttendanceRoles.Manager, AttendanceRoles.HRAdministrator, AttendanceRoles.PayrollAdministrator, AttendanceRoles.IntegrationAdministrator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(method, url, TestApi.As(7, role), body)).StatusCode);
        }

        Assert.Empty(_formats.Calls);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await Send(method, url, Platform, body)).StatusCode);
    }

    [Fact]
    public async Task ThePreview_IsForPlatformAdministratorsOnly()
    {
        using var content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "sample.csv" }, { new StringContent("""{"name":"Mine"}"""), "format" } };
        using var asTenant = new HttpRequestMessage(HttpMethod.Post, "api/attendance/payload-formats/preview") { Content = content };
        asTenant.Headers.Add("X-Test-User", Tenant);

        Assert.Equal(HttpStatusCode.Forbidden, (await _api.Client.SendAsync(asTenant)).StatusCode);
        Assert.Empty(_formats.Calls);
    }

    [Fact]
    public async Task AnExampleOfASavedFormat_IsMadeFromTheStoredFormat_InTheKindAsked_AndAnUnknownKindOrFormatIsRefused()
    {
        var csv = await Send("GET", "api/attendance/payload-formats/1/sample?kind=csv", Tenant, null);
        var json = await Send("GET", "api/attendance/payload-formats/1/sample?kind=json", Tenant, null);
        var badKind = await Send("GET", "api/attendance/payload-formats/1/sample?kind=pdf", Tenant, null);
        _formats.Exists = false;
        var missing = await Send("GET", "api/attendance/payload-formats/9/sample?kind=csv", Tenant, null);

        var sample = (await csv.Content.ReadFromJsonAsync<PayloadFormatSample>())!;
        Assert.Equal("csv", sample.Kind);
        Assert.Contains("Badge", sample.Content);
        Assert.Equal("json", (await json.Content.ReadFromJsonAsync<PayloadFormatSample>())!.Kind);
        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ASaveFromATenantThatIsNotThePrimeOne_IsAnswered403WithTheCatalogReadOnlyCode_EvenForAPlatformAdministrator()
    {
        _formats.Throw = new robeem.Application.Attendance.Catalog.CatalogReadOnlyException();

        var response = await Send("POST", "api/attendance/payload-formats", Platform, """{"name":"Mine"}""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("catalog_read_only", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Extensions["code"]!.ToString());
    }

    private Task<HttpResponseMessage> Send(string method, string url, string? user, string? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (user is not null)
        {
            request.Headers.Add("X-Test-User", user);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return _api.Client.SendAsync(request);
    }

    private sealed class FakeFormats : IAttendancePayloadFormatService
    {
        public List<string> Calls { get; } = [];

        public bool Exists { get; set; } = true;

        public Exception? Throw { get; set; }

        public Task<IReadOnlyList<PayloadFormatListItem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PayloadFormatListItem>>([]);

        public Task<IReadOnlyList<PayloadFormatOption>> ListOptionsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PayloadFormatOption>>([]);

        public Task<PayloadFormatInput?> GetInputAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<PayloadFormatInput?>(Exists
                ? new PayloadFormatInput { Name = "Branch export", EmployeeColumn = "Badge", TimestampColumn = "Time", TimestampFormats = "yyyy-MM-dd HH:mm" }
                : null);

        public Task<SaveResult> CreateAsync(PayloadFormatInput input, CancellationToken cancellationToken)
        {
            Calls.Add("create");
            return Throw is null ? Task.FromResult(SaveResult.Success(1)) : Task.FromException<SaveResult>(Throw);
        }

        public Task<SaveResult> UpdateAsync(int id, PayloadFormatInput input, CancellationToken cancellationToken)
        {
            Calls.Add("update");
            return Task.FromResult(SaveResult.Success(id));
        }

        public Task<PayloadFormatPreview> PreviewAsync(PayloadFormatInput input, string filePath, CancellationToken cancellationToken)
        {
            Calls.Add("preview");
            return Task.FromResult(new PayloadFormatPreview());
        }
    }
}
