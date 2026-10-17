using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Tests.Api;

/// <summary>The provider, device type and transport endpoints: integration administrators only, and how a refusal is answered.</summary>
public sealed class CatalogEndpointTests : IAsyncDisposable
{
    private static readonly string Admin = TestApi.As(1, AttendanceRoles.IntegrationAdministrator);

    private readonly FakeProviders _providers = new();
    private readonly FakeDeviceTypes _types = new();
    private readonly FakeTransports _transports = new();
    private readonly TestApi _api;

    public CatalogEndpointTests()
    {
        _api = TestApi.Start(services =>
        {
            services.AddSingleton<IAttendanceProviderService>(_providers);
            services.AddSingleton<IAttendanceDeviceTypeService>(_types);
            services.AddSingleton<IAttendanceTransportService>(_transports);
        });
    }

    public static IEnumerable<object[]> Endpoints() =>
    [
        ["GET", "api/attendance/providers", null!],
        ["GET", "api/attendance/providers/1/input", null!],
        ["POST", "api/attendance/providers", """{"providerKey":"acme","displayName":"Acme"}"""],
        ["PUT", "api/attendance/providers/1", """{"providerKey":"acme","displayName":"Acme"}"""],
        ["GET", "api/attendance/device-types", null!],
        ["GET", "api/attendance/device-types/options", null!],
        ["GET", "api/attendance/device-types/1/input", null!],
        ["POST", "api/attendance/device-types", """{"name":"Palm reader"}"""],
        ["PUT", "api/attendance/device-types/1", """{"name":"Palm reader"}"""],
        ["GET", "api/attendance/transports", null!],
        ["GET", "api/attendance/transports/1/input", null!],
        ["PUT", "api/attendance/transports/1", """{"displayName":"Secure FTP"}"""],
    ];

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task EveryEndpoint_IsForIntegrationAdministratorsOnly(string method, string url, string? body)
    {
        foreach (var role in new[] { AttendanceRoles.Employee, AttendanceRoles.Manager, AttendanceRoles.HRAdministrator, AttendanceRoles.PayrollAdministrator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(method, url, TestApi.As(7, role), body)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(method, url, null, body)).StatusCode);
        Assert.Empty(CallsOfAll());

        Assert.NotEqual(HttpStatusCode.Forbidden, (await Send(method, url, Admin, body)).StatusCode);
        Assert.NotEmpty(CallsOfAll());
    }

    [Fact]
    public async Task Create_Answers201WithTheId_AndARefusalAs400KeyedByTheInputsProperties()
    {
        _providers.Result = SaveResult.Success(42);
        var created = await Send("POST", "api/attendance/providers", Admin, """{"providerKey":"acme","displayName":"Acme"}""");

        _providers.Result = SaveResult.Invalid("ProviderKey", "Another provider already has this key.");
        var refused = await Send("POST", "api/attendance/providers", Admin, """{"providerKey":"acme","displayName":"Acme"}""");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(42, (await created.Content.ReadFromJsonAsync<CreatedResponse>())!.Id);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("ProviderKey", (await refused.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
    }

    [Fact]
    public async Task AProviderKey_ThatIsNotLowerCaseLettersDigitsAndSeparators_IsRefusedBeforeTheServiceIsCalled()
    {
        var response = await Send("POST", "api/attendance/providers", Admin, """{"providerKey":"Has Spaces","displayName":"Acme"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_providers.Calls);
    }

    [Fact]
    public async Task UpdatingAMissingRow_Is404_ForEachKindOfRow()
    {
        _providers.Result = _types.Result = _transports.Result = SaveResult.Missing();

        Assert.Equal(HttpStatusCode.NotFound, (await Send("PUT", "api/attendance/providers/9", Admin, """{"providerKey":"acme","displayName":"Acme"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("PUT", "api/attendance/device-types/9", Admin, """{"name":"x"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("PUT", "api/attendance/transports/9", Admin, """{"displayName":"x"}""")).StatusCode);
    }

    [Fact]
    public async Task AProviderDeviceTypeOrTransportThatDoesNotExist_Is404_OnItsInput()
    {
        _providers.Exists = _types.Exists = _transports.Exists = false;

        Assert.Equal(HttpStatusCode.NotFound, (await Send("GET", "api/attendance/providers/9/input", Admin, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("GET", "api/attendance/device-types/9/input", Admin, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("GET", "api/attendance/transports/9/input", Admin, null)).StatusCode);
    }

    private List<string> CallsOfAll() => [.. _providers.Calls, .. _types.Calls, .. _transports.Calls];

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

    private sealed class FakeProviders : IAttendanceProviderService
    {
        public List<string> Calls { get; } = [];

        public SaveResult Result { get; set; } = SaveResult.Success(1);

        public bool Exists { get; set; } = true;

        public Task<IReadOnlyList<ProviderListItem>> ListAsync(CancellationToken cancellationToken)
        {
            Calls.Add("list");
            return Task.FromResult<IReadOnlyList<ProviderListItem>>([]);
        }

        public Task<ProviderInput?> GetInputAsync(int id, CancellationToken cancellationToken)
        {
            Calls.Add("input");
            return Task.FromResult<ProviderInput?>(Exists ? new ProviderInput() : null);
        }

        public Task<SaveResult> CreateAsync(ProviderInput input, CancellationToken cancellationToken)
        {
            Calls.Add("create");
            return Task.FromResult(Result);
        }

        public Task<SaveResult> UpdateAsync(int id, ProviderInput input, CancellationToken cancellationToken)
        {
            Calls.Add("update");
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeDeviceTypes : IAttendanceDeviceTypeService
    {
        public List<string> Calls { get; } = [];

        public SaveResult Result { get; set; } = SaveResult.Success(1);

        public bool Exists { get; set; } = true;

        public Task<IReadOnlyList<DeviceTypeItem>> ListAsync(CancellationToken cancellationToken)
        {
            Calls.Add("list");
            return Task.FromResult<IReadOnlyList<DeviceTypeItem>>([]);
        }

        public Task<IReadOnlyList<DeviceTypeOption>> ListOptionsAsync(CancellationToken cancellationToken)
        {
            Calls.Add("options");
            return Task.FromResult<IReadOnlyList<DeviceTypeOption>>([]);
        }

        public Task<DeviceTypeInput?> GetInputAsync(int id, CancellationToken cancellationToken)
        {
            Calls.Add("input");
            return Task.FromResult<DeviceTypeInput?>(Exists ? new DeviceTypeInput() : null);
        }

        public Task<SaveResult> CreateAsync(DeviceTypeInput input, CancellationToken cancellationToken)
        {
            Calls.Add("create");
            return Task.FromResult(Result);
        }

        public Task<SaveResult> UpdateAsync(int id, DeviceTypeInput input, CancellationToken cancellationToken)
        {
            Calls.Add("update");
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeTransports : IAttendanceTransportService
    {
        public List<string> Calls { get; } = [];

        public SaveResult Result { get; set; } = SaveResult.Success(1);

        public bool Exists { get; set; } = true;

        public Task<IReadOnlyList<TransportItem>> ListAsync(CancellationToken cancellationToken)
        {
            Calls.Add("list");
            return Task.FromResult<IReadOnlyList<TransportItem>>([]);
        }

        public Task<TransportInput?> GetInputAsync(int id, CancellationToken cancellationToken)
        {
            Calls.Add("input");
            return Task.FromResult<TransportInput?>(Exists ? new TransportInput() : null);
        }

        public Task<SaveResult> UpdateAsync(int id, TransportInput input, CancellationToken cancellationToken)
        {
            Calls.Add("update");
            return Task.FromResult(Result);
        }
    }
}
