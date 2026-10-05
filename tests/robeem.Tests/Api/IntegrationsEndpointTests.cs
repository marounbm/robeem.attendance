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

/// <summary>The integrations endpoints (screens phase 2): roles, status codes and how a refusal is answered.</summary>
public sealed class IntegrationsEndpointTests : IAsyncDisposable
{
    private static readonly string Admin = TestApi.As(1, AttendanceRoles.IntegrationAdministrator);

    private readonly FakeIntegrations _integrations = new();
    private readonly TestApi _api;

    public IntegrationsEndpointTests()
    {
        _api = TestApi.Start(services =>
        {
            services.AddSingleton<IAttendanceIntegrationService>(_integrations);
            services.AddSingleton<IIntegrationHealthService, FakeHealth>();
        });
    }

    public static IEnumerable<object[]> Endpoints() =>
    [
        ["GET", "api/attendance/integrations", null!],
        ["GET", "api/attendance/integrations/providers", null!],
        ["GET", "api/attendance/integrations/employee-options", null!],
        ["GET", "api/attendance/integrations/1", null!],
        ["GET", "api/attendance/integrations/1/input", null!],
        ["POST", "api/attendance/integrations", """{"providerId":1,"displayName":"x"}"""],
        ["PUT", "api/attendance/integrations/1", """{"providerId":1,"displayName":"x"}"""],
        ["GET", "api/attendance/integrations/1/devices", null!],
        ["POST", "api/attendance/integrations/1/devices", """{"externalDeviceId":"d","name":"n","timeZoneId":"UTC","deviceType":"Biometric"}"""],
        ["PUT", "api/attendance/integrations/1/devices/2", """{"externalDeviceId":"d","name":"n","timeZoneId":"UTC","deviceType":"Biometric"}"""],
        ["DELETE", "api/attendance/integrations/1/devices/2", null!],
        ["GET", "api/attendance/integrations/1/mappings", null!],
        ["POST", "api/attendance/integrations/1/mappings", """{"externalEmployeeId":"e","employeeId":1}"""],
        ["DELETE", "api/attendance/integrations/1/mappings/2", null!],
        ["POST", "api/attendance/integrations/1/sync", null!],
        ["POST", "api/attendance/integrations/1/reconcile", null!],
        ["GET", "api/attendance/integrations/1/health", null!],
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
        Assert.Empty(_integrations.Calls);

        // And the administrator gets through (to the fake, whatever it answers).
        Assert.NotEqual(HttpStatusCode.Forbidden, (await Send(method, url, Admin, body)).StatusCode);
        if (!url.EndsWith("/health", StringComparison.Ordinal))
        {
            Assert.NotEmpty(_integrations.Calls);
        }
    }

    [Fact]
    public async Task Create_Answers201WithTheId_AndARefusalAs400KeyedByTheInputsProperties()
    {
        _integrations.Result = SaveResult.Success(42);
        var created = await Send("POST", "api/attendance/integrations", Admin, """{"providerId":1,"displayName":"HQ"}""");

        _integrations.Result = SaveResult.Invalid("ConnectionConfigRef", "Give the name the secret is kept under.");
        var refused = await Send("POST", "api/attendance/integrations", Admin, """{"providerId":1,"displayName":"HQ"}""");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(42, (await created.Content.ReadFromJsonAsync<CreatedResponse>())!.Id);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("ConnectionConfigRef", problem!.Errors.Keys);
    }

    [Fact]
    public async Task UpdateAndDelete_Answer204_404_Or400()
    {
        _integrations.Result = SaveResult.Success(1);
        Assert.Equal(HttpStatusCode.NoContent, (await Send("PUT", "api/attendance/integrations/1", Admin, """{"providerId":1,"displayName":"HQ"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send("DELETE", "api/attendance/integrations/1/devices/2", Admin, null)).StatusCode);

        _integrations.Result = SaveResult.Missing();
        Assert.Equal(HttpStatusCode.NotFound, (await Send("PUT", "api/attendance/integrations/9", Admin, """{"providerId":1,"displayName":"HQ"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("DELETE", "api/attendance/integrations/1/mappings/9", Admin, null)).StatusCode);

        _integrations.Result = SaveResult.Invalid("id", "This device has punches on record, so it can't be deleted.");
        var refused = await Send("DELETE", "api/attendance/integrations/1/devices/2", Admin, null);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("punches on record", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AMissingIntegration_IsA404_ForEveryRead()
    {
        _integrations.Exists = false;

        foreach (var url in new[] { "api/attendance/integrations/9", "api/attendance/integrations/9/input", "api/attendance/integrations/9/devices", "api/attendance/integrations/9/mappings", "api/attendance/integrations/9/health" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Send("GET", url, Admin, null)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await Send("POST", "api/attendance/integrations/9/sync", Admin, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("POST", "api/attendance/integrations/9/reconcile", Admin, null)).StatusCode);
    }

    [Fact]
    public async Task RunNow_Is200WithTheOutcome_EvenWhenTheProviderFailed()
    {
        _integrations.Outcome = new SyncOutcomeResponse { Status = "Failed", Message = "vendor is down" };

        var sync = await Send("POST", "api/attendance/integrations/1/sync", Admin, null);
        var reconcile = await Send("POST", "api/attendance/integrations/1/reconcile", Admin, null);

        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        Assert.Equal("vendor is down", (await sync.Content.ReadFromJsonAsync<SyncOutcomeResponse>())!.Message);
        Assert.Equal(HttpStatusCode.OK, reconcile.StatusCode);
        Assert.Equal([("run", false), ("run", true)], _integrations.Runs);
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

    private sealed class FakeIntegrations : IAttendanceIntegrationService
    {
        public List<string> Calls { get; } = [];

        public List<(string Kind, bool Reconcile)> Runs { get; } = [];

        public SaveResult Result { get; set; } = SaveResult.Success(1);

        public bool Exists { get; set; } = true;

        public SyncOutcomeResponse Outcome { get; set; } = new() { Status = "Succeeded" };

        public Task<IReadOnlyList<IntegrationListItem>> ListAsync(CancellationToken cancellationToken) => Record<IReadOnlyList<IntegrationListItem>>("list", []);

        public Task<IntegrationListItem?> GetAsync(int id, CancellationToken cancellationToken) => Record<IntegrationListItem?>("get", Exists ? new IntegrationListItem { Id = id } : null);

        public Task<IntegrationInput?> GetInputAsync(int id, CancellationToken cancellationToken) => Record<IntegrationInput?>("input", Exists ? new IntegrationInput() : null);

        public Task<IReadOnlyList<ProviderOption>> ListProvidersAsync(CancellationToken cancellationToken) => Record<IReadOnlyList<ProviderOption>>("providers", []);

        public Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken) => Record<IReadOnlyList<EmployeeOption>>("employeeOptions", []);

        public Task<SaveResult> CreateAsync(IntegrationInput input, CancellationToken cancellationToken) => Record("create", Result);

        public Task<SaveResult> UpdateAsync(int id, IntegrationInput input, CancellationToken cancellationToken) => Record("update", Result);

        public Task<IReadOnlyList<DeviceItem>?> ListDevicesAsync(int integrationId, CancellationToken cancellationToken) => Record<IReadOnlyList<DeviceItem>?>("devices", Exists ? [] : null);

        public Task<SaveResult> AddDeviceAsync(int integrationId, DeviceInput input, CancellationToken cancellationToken) => Record("addDevice", Result);

        public Task<SaveResult> UpdateDeviceAsync(int integrationId, int deviceId, DeviceInput input, CancellationToken cancellationToken) => Record("updateDevice", Result);

        public Task<SaveResult> DeleteDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken) => Record("deleteDevice", Result);

        public Task<IReadOnlyList<MappingItem>?> ListMappingsAsync(int integrationId, CancellationToken cancellationToken) => Record<IReadOnlyList<MappingItem>?>("mappings", Exists ? [] : null);

        public Task<SaveResult> AddMappingAsync(int integrationId, MappingInput input, CancellationToken cancellationToken) => Record("addMapping", Result);

        public Task<SaveResult> DeleteMappingAsync(int integrationId, int mappingId, CancellationToken cancellationToken) => Record("deleteMapping", Result);

        public Task<SyncOutcomeResponse?> RunAsync(int integrationId, bool reconcile, CancellationToken cancellationToken)
        {
            Runs.Add(("run", reconcile));
            return Record<SyncOutcomeResponse?>("run", Exists ? Outcome : null);
        }

        private Task<T> Record<T>(string call, T value)
        {
            Calls.Add(call);
            return Task.FromResult(value);
        }
    }

    private sealed class FakeHealth : IIntegrationHealthService
    {
        public Task<IntegrationHealthReport?> GetIntegrationHealthAsync(int integrationId, CancellationToken cancellationToken) =>
            Task.FromResult<IntegrationHealthReport?>(integrationId == 9 ? null : new IntegrationHealthReport { IntegrationId = integrationId });
    }
}
