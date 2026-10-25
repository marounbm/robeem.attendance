using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;

namespace robeem.Tests.Web;

/// <summary>The integrations screens (screens phase 2): the typed client and the controller.</summary>
public sealed class IntegrationsWebTests
{
    private const string Refusal = """
        {"title":"One or more validation errors occurred.","status":400,
         "errors":{"ConnectionConfigRef":["Give the name the secret is kept under."],"id":["This device has punches on record."]}}
        """;

    [Fact]
    public async Task TheClient_ReadsListsAndSingleRecords_AndMapsA404ToNull()
    {
        var list = new StubHttpHandler(HttpStatusCode.OK, """[{"id":1,"displayName":"HQ","vendorName":"Generic CSV","deviceCount":2,"mappingCount":5}]""");
        var missing = new IntegrationsApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient());

        var items = await new IntegrationsApiClient(list.CreateClient()).ListAsync(CancellationToken.None);

        Assert.Equal((2, 5), (items[0].DeviceCount, items[0].MappingCount));
        Assert.Equal("/api/attendance/integrations", list.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Null(await missing.GetAsync(9, CancellationToken.None));
        Assert.Null(await missing.GetInputAsync(9, CancellationToken.None));
        Assert.Null(await missing.GetHealthAsync(9, CancellationToken.None));
    }

    [Theory]
    [InlineData("connectors", "/api/attendance/integrations/connectors")]
    [InlineData("employees", "/api/attendance/integrations/employee-options")]
    public async Task TheClient_AsksTheRightPathForTheOptions(string which, string path)
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, "[]");
        var client = new IntegrationsApiClient(handler.CreateClient());

        if (which == "connectors")
        {
            await client.ListConnectorsAsync(CancellationToken.None);
        }
        else
        {
            await client.ListEmployeeOptionsAsync(CancellationToken.None);
        }

        Assert.Equal(path, handler.Requests.Single().RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Create_Returns201AsTheNewId_AndARefusalAsFieldErrors()
    {
        var created = new IntegrationsApiClient(new StubHttpHandler(HttpStatusCode.Created, """{"id":7}""").CreateClient());
        var refused = new IntegrationsApiClient(new StubHttpHandler(HttpStatusCode.BadRequest, Refusal, "application/problem+json").CreateClient());

        var ok = await created.CreateAsync(new IntegrationInput { DisplayName = "HQ" }, CancellationToken.None);
        var no = await refused.CreateAsync(new IntegrationInput { DisplayName = "HQ" }, CancellationToken.None);

        Assert.Equal((true, 7), (ok.Succeeded, ok.Id));
        Assert.False(no.Succeeded);
        Assert.Equal("Give the name the secret is kept under.", no.Errors[nameof(IntegrationInput.ConnectionConfigRef)]);
    }

    [Fact]
    public async Task UpdateAndDelete_Return204_404_Or400()
    {
        var ok = new IntegrationsApiClient(new StubHttpHandler(HttpStatusCode.NoContent).CreateClient());
        var gone = new IntegrationsApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient());
        var refused = new IntegrationsApiClient(new StubHttpHandler(HttpStatusCode.BadRequest, Refusal, "application/problem+json").CreateClient());

        Assert.Equal((true, 3), ((await ok.UpdateAsync(3, new IntegrationInput(), CancellationToken.None)).Succeeded, (await ok.UpdateAsync(3, new IntegrationInput(), CancellationToken.None)).Id));
        Assert.True((await ok.DeleteDeviceAsync(1, 2, CancellationToken.None)).Succeeded);
        Assert.True((await gone.DeleteMappingAsync(1, 2, 3, CancellationToken.None)).NotFound);
        var no = await refused.DeleteDeviceAsync(1, 2, CancellationToken.None);
        Assert.False(no.Succeeded);

        // The Api's "id" key names no input property, so it becomes the model-level message.
        Assert.Contains("punches on record", no.Errors[string.Empty]);
        Assert.Contains("punches on record", no.MessageText);
    }

    [Fact]
    public async Task TheClient_UsesTheRoutesOfDevicesMappingsAndRunNow()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """{"status":"Succeeded","batchId":4,"recordsIngested":3}""");
        var client = new IntegrationsApiClient(handler.CreateClient());

        await client.RunAsync(5, reconcile: false, CancellationToken.None);
        await client.RunAsync(5, reconcile: true, CancellationToken.None);
        await client.UpdateDeviceAsync(5, 8, new DeviceInput(), CancellationToken.None);
        await client.AddMappingAsync(5, 8, new MappingInput(), CancellationToken.None);
        await client.DeleteMappingAsync(5, 8, 3, CancellationToken.None);

        Assert.Equal(
            [
                ("POST", "/api/attendance/integrations/5/sync"),
                ("POST", "/api/attendance/integrations/5/reconcile"),
                ("PUT", "/api/attendance/integrations/5/devices/8"),
                ("POST", "/api/attendance/integrations/5/devices/8/mappings"),
                ("DELETE", "/api/attendance/integrations/5/devices/8/mappings/3"),
            ],
            handler.Requests.Select(r => (r.Method.Method, r.RequestUri!.PathAndQuery)));
    }

    [Fact]
    public async Task TheClient_ReadsTheGridOfAllDevices_ADevice_AndADevicesMappings()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, "[]");
        var client = new IntegrationsApiClient(handler.CreateClient());
        var single = new StubHttpHandler(HttpStatusCode.OK, """{"id":8,"integrationId":5,"integrationName":"HQ","mappingCount":2}""");

        Assert.Empty(await client.ListAllDevicesAsync(CancellationToken.None));
        Assert.Empty(await client.ListMappingsAsync(5, 8, CancellationToken.None));
        var device = await new IntegrationsApiClient(single.CreateClient()).GetDeviceAsync(5, 8, CancellationToken.None);
        Assert.Null(await new IntegrationsApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient()).GetDeviceAsync(5, 9, CancellationToken.None));

        Assert.Equal(["/api/attendance/integrations/devices", "/api/attendance/integrations/5/devices/8/mappings"], handler.Requests.Select(r => r.RequestUri!.PathAndQuery));
        Assert.Equal("/api/attendance/integrations/5/devices/8", single.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal((8, 5, "HQ", 2), (device!.Id, device.IntegrationId, device.IntegrationName, device.MappingCount));
    }

    [Fact]
    public async Task Creating_RedirectsToTheNewIntegration_OrShowsTheFormWithItsErrors()
    {
        var integrations = new FakeIntegrations { Save = ApiSaveResult.Success(7) };
        var controller = WithTempData(new IntegrationsController(integrations, new FakeConnections()));

        var created = await controller.Create(new IntegrationFormViewModel { Input = new IntegrationInput { DisplayName = "HQ" } }, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(created);
        Assert.Equal(("Details", 7), (redirect.ActionName, redirect.RouteValues!["id"]));
        Assert.Contains("'HQ' was created", (string)controller.TempData["StatusMessage"]!);

        integrations.Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [nameof(IntegrationInput.PollingScheduleCron)] = "Not a valid cron expression." });
        var again = await controller.Create(new IntegrationFormViewModel { Input = new IntegrationInput { DisplayName = "HQ" } }, CancellationToken.None);

        Assert.IsType<ViewResult>(again);
        Assert.Equal("Not a valid cron expression.", controller.ModelState["Input.PollingScheduleCron"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task EditingAnUnknownIntegration_IsNotFound()
    {
        var controller = WithTempData(new IntegrationsController(new FakeIntegrations { Save = ApiSaveResult.Missing() }, new FakeConnections()));

        Assert.IsType<NotFoundResult>(await controller.Edit(9, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Details(9, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Edit(9, new IntegrationFormViewModel(), CancellationToken.None));
    }

    [Fact]
    public async Task TheDevicesGrid_ListsEveryDevice_OrOnlyThoseOfTheChosenIntegration()
    {
        var integrations = new FakeIntegrations();
        integrations.Devices.AddRange([new DeviceItem { Id = 1, IntegrationId = 5 }, new DeviceItem { Id = 2, IntegrationId = 6 }]);
        var controller = WithTempData(new DevicesController(integrations, new FakeConnections()));

        var all = Assert.IsType<DevicesIndexViewModel>(Assert.IsType<ViewResult>(await controller.Index(null, CancellationToken.None)).Model);
        var one = Assert.IsType<DevicesIndexViewModel>(Assert.IsType<ViewResult>(await controller.Index(6, CancellationToken.None)).Model);

        Assert.Equal([1, 2], all.Devices.Select(d => d.Id));
        Assert.Equal([2], one.Devices.Select(d => d.Id));
        Assert.Equal(6, one.IntegrationId);
    }

    [Fact]
    public async Task ADeviceAction_ReportsItsOutcome_AndABadFormIsNeverSent()
    {
        var integrations = new FakeIntegrations { Save = ApiSaveResult.Success(1) };
        var controller = WithTempData(new DevicesController(integrations, new FakeConnections()));

        var added = Assert.IsType<RedirectToActionResult>(await controller.Create(5, new DeviceFormViewModel { Input = new DeviceInput { Name = "Front door" } }, CancellationToken.None));
        Assert.Equal(("Edit", 1), (added.ActionName, added.RouteValues!["id"]));
        Assert.Contains("'Front door' was added", (string)controller.TempData["StatusMessage"]!);

        integrations.Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "This device has punches on record." });
        var refused = Assert.IsType<RedirectToActionResult>(await controller.Delete(5, 2, CancellationToken.None));
        Assert.Equal("Edit", refused.ActionName);
        Assert.Equal("This device has punches on record.", controller.TempData["ErrorMessage"]);

        controller.ModelState.AddModelError("Name", "required");
        var calls = integrations.Calls.Count;
        Assert.IsType<ViewResult>(await controller.Create(5, new DeviceFormViewModel(), CancellationToken.None));
        await controller.AddMapping(5, 2, new MappingInput(), CancellationToken.None);
        Assert.Equal(calls, integrations.Calls.Count);
        Assert.Equal("Fill in every required field.", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task AMapping_IsAddedAndRemovedOnItsDevice_AndAnUnknownDeviceIsNotFound()
    {
        var integrations = new FakeIntegrations { Save = ApiSaveResult.Success(1) };
        var controller = WithTempData(new DevicesController(integrations, new FakeConnections()));

        var added = Assert.IsType<RedirectToActionResult>(await controller.AddMapping(5, 2, new MappingInput { ExternalEmployeeId = "21001", EmployeeId = 7 }, CancellationToken.None));
        Assert.Equal(("Edit", 5, 2), (added.ActionName, added.RouteValues!["integrationId"], added.RouteValues["id"]));
        Assert.Equal("Vendor id '21001' was mapped.", controller.TempData["StatusMessage"]);

        await controller.DeleteMapping(5, 2, 3, CancellationToken.None);
        Assert.Equal("The mapping was removed.", controller.TempData["StatusMessage"]);
        Assert.Equal(["addMapping", "deleteMapping"], integrations.Calls);

        integrations.Save = ApiSaveResult.Missing();
        Assert.IsType<NotFoundResult>(await controller.Edit(5, 9, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.AddMapping(5, 9, new MappingInput { ExternalEmployeeId = "1", EmployeeId = 1 }, CancellationToken.None));
    }

    [Theory]
    [InlineData("Succeeded", "StatusMessage")]
    [InlineData("Skipped", "StatusMessage")]
    [InlineData("Failed", "ErrorMessage")]
    [InlineData("CircuitOpen", "ErrorMessage")]
    public async Task RunNow_ShowsAFailureAsAnError_AndTheRestAsInformation(string status, string key)
    {
        var integrations = new FakeIntegrations { Outcome = new SyncOutcomeResponse { Status = status, RecordsIngested = 3, Message = "note" } };
        var controller = WithTempData(new IntegrationsController(integrations, new FakeConnections()));

        await controller.Run(5, reconcile: true, CancellationToken.None);

        Assert.Contains($"Reconciliation: {status}, 3 record(s). note", (string)controller.TempData[key]!);
    }

    [Fact]
    public async Task TheClient_AsksForTheRawMappingWithItsFilter_AndMapsA404ToNull()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """[{"rawId":4,"rawStatus":"Failed","processingError":"Unparseable timestamp 'x'","payload":"{}"}]""");
        var missing = new IntegrationsApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient());

        var rows = await new IntegrationsApiClient(handler.CreateClient()).ListRawMappingAsync(5, 7, "Failed", 50, CancellationToken.None);

        Assert.Equal("/api/attendance/integrations/5/raw-mapping?take=50&batchId=7&status=Failed", handler.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal(("Failed", "Unparseable timestamp 'x'"), (rows![0].RawStatus, rows[0].ProcessingError));
        Assert.Null(rows[0].NormalizedId);
        Assert.Null(await missing.ListRawMappingAsync(9, null, null, 100, CancellationToken.None));
        Assert.Null(await missing.GetRawMappingGuideAsync(9, CancellationToken.None));
    }

    [Fact]
    public async Task TheRawMappingPage_ClampsHowManyRowsItAsksFor_AndAnUnknownIntegrationIsNotFound()
    {
        var integrations = new FakeIntegrations();
        var controller = WithTempData(new IntegrationsController(integrations, new FakeConnections()));

        var page = await controller.RawMapping(5, null, null, 100000, CancellationToken.None);
        await controller.RawMapping(5, null, null, 0, CancellationToken.None);

        Assert.IsType<ViewResult>(page);
        Assert.Equal([500, 100], integrations.RawMappingTakes);

        integrations.Save = ApiSaveResult.Missing();
        Assert.IsType<NotFoundResult>(await controller.RawMapping(9, null, null, 100, CancellationToken.None));
    }

    [Fact]
    public async Task TheClient_PostsRetryFailed_AndMapsA404ToNull()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """{"requeued":4}""");
        var missing = new IntegrationsApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient());

        var result = await new IntegrationsApiClient(handler.CreateClient()).RetryFailedAsync(5, CancellationToken.None);

        Assert.Equal(("POST", "/api/attendance/integrations/5/retry-failed"), (handler.Requests.Single().Method.Method, handler.Requests.Single().RequestUri!.PathAndQuery));
        Assert.Equal(4, result!.Requeued);
        Assert.Null(await missing.RetryFailedAsync(9, CancellationToken.None));
    }

    [Fact]
    public async Task RetryFailed_RedirectsToTheIntegrationWithHowManyWereQueued_OrIsNotFound()
    {
        var integrations = new FakeIntegrations();
        var controller = WithTempData(new IntegrationsController(integrations, new FakeConnections()));

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.RetryFailed(5, CancellationToken.None));

        Assert.Equal((nameof(IntegrationsController.Details), 5), (redirect.ActionName, redirect.RouteValues!["id"]));
        Assert.Contains("3 punch(es)", (string)controller.TempData["StatusMessage"]!);

        integrations.Save = ApiSaveResult.Missing();
        Assert.IsType<NotFoundResult>(await controller.RetryFailed(9, CancellationToken.None));
    }

    [Fact]
    public async Task TheClient_PostsRenormalizeFlagged_AndMapsA404ToNull()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """{"requeued":6}""");
        var missing = new IntegrationsApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient());

        var result = await new IntegrationsApiClient(handler.CreateClient()).RenormalizeFlaggedAsync(5, CancellationToken.None);

        Assert.Equal(("POST", "/api/attendance/integrations/5/renormalize-flagged"), (handler.Requests.Single().Method.Method, handler.Requests.Single().RequestUri!.PathAndQuery));
        Assert.Equal(6, result!.Requeued);
        Assert.Null(await missing.RenormalizeFlaggedAsync(9, CancellationToken.None));
    }

    [Fact]
    public async Task RenormalizeFlagged_RedirectsToTheIntegrationWithHowManyWereQueued_OrIsNotFound()
    {
        var integrations = new FakeIntegrations();
        var controller = WithTempData(new IntegrationsController(integrations, new FakeConnections()));

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.RenormalizeFlagged(5, CancellationToken.None));

        Assert.Equal((nameof(IntegrationsController.Details), 5), (redirect.ActionName, redirect.RouteValues!["id"]));
        Assert.Contains("2 punch(es)", (string)controller.TempData["StatusMessage"]!);

        integrations.Save = ApiSaveResult.Missing();
        Assert.IsType<NotFoundResult>(await controller.RenormalizeFlagged(9, CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 0, "FileUpload", "StatusMessage", "No punch of this integration is flagged")]
    [InlineData(0, 4, "FileUpload", "ErrorMessage", "choose one under Device for uploaded files")]
    [InlineData(0, 4, "RestApi", "ErrorMessage", "not manual file upload")]
    [InlineData(2, 3, "FileUpload", "StatusMessage", "3 other flagged punch(es)")]
    public async Task RenormalizeFlagged_SaysWhyNothingWasQueued(int requeued, int unresolved, string transport, string key, string fragment)
    {
        var integrations = new FakeIntegrations { Renormalized = new RenormalizeFlaggedResponse { Requeued = requeued, Unresolved = unresolved, Transport = transport } };
        var controller = WithTempData(new IntegrationsController(integrations, new FakeConnections()));

        await controller.RenormalizeFlagged(5, CancellationToken.None);

        Assert.Contains(fragment, (string)controller.TempData[key]!);
    }

    [Theory]
    [InlineData(0.9512, "95.1%")]
    [InlineData(0.0, "0%")]
    [InlineData(null, "—")]
    public void SharesAreWrittenAsPercentages(double? share, string expected)
    {
        Assert.Equal(expected, DisplayFormats.Percent(share));
    }

    [Theory]
    [InlineData(45.0, "45 s")]
    [InlineData(300.0, "5 min")]
    [InlineData(7200.0, "2 h")]
    [InlineData(null, "—")]
    public void DelaysAreWrittenInTheRightUnit(double? seconds, string expected)
    {
        Assert.Equal(expected, DisplayFormats.Delay(seconds));
    }

    private static T WithTempData<T>(T controller)
        where T : Controller
    {
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider());
        return controller;
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class FakeConnections : IConnectionsApiClient
    {
        public Task<IReadOnlyList<ConnectionListItem>?> ListAsync(int integrationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConnectionListItem>?>([]);

        public Task<ConnectionInput?> GetInputAsync(int integrationId, int connectionId, CancellationToken cancellationToken) =>
            Task.FromResult<ConnectionInput?>(new ConnectionInput { Name = "Default" });

        public Task<ApiSaveResult> CreateAsync(int integrationId, ConnectionInput input, CancellationToken cancellationToken) =>
            Task.FromResult(ApiSaveResult.Success(1));

        public Task<ApiSaveResult> UpdateAsync(int integrationId, int connectionId, ConnectionInput input, CancellationToken cancellationToken) =>
            Task.FromResult(ApiSaveResult.Success(connectionId));

        public Task<ApiSaveResult> MakeDefaultAsync(int integrationId, int connectionId, CancellationToken cancellationToken) =>
            Task.FromResult(ApiSaveResult.Success(connectionId));

        public Task<ConnectionTestResult?> TestAsync(int integrationId, int connectionId, CancellationToken cancellationToken) =>
            Task.FromResult<ConnectionTestResult?>(new ConnectionTestResult { Succeeded = true, Message = "ok" });

        public Task<IReadOnlyList<TransportDescriptorItem>> ListDescriptorsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TransportDescriptorItem>>([]);
    }

    private sealed class FakeIntegrations : IIntegrationsApiClient
    {
        public ApiSaveResult Save { get; set; } = ApiSaveResult.Success(1);

        public SyncOutcomeResponse Outcome { get; set; } = new() { Status = "Succeeded" };

        public RenormalizeFlaggedResponse Renormalized { get; set; } = new() { Requeued = 2, Transport = "FileUpload" };

        public List<string> Calls { get; } = [];

        public List<int> RawMappingTakes { get; } = [];

        public List<DeviceItem> Devices { get; } = [];

        public Task<IReadOnlyList<IntegrationListItem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<IntegrationListItem>>([]);

        public Task<IntegrationListItem?> GetAsync(int id, CancellationToken cancellationToken) => Task.FromResult<IntegrationListItem?>(Save.NotFound ? null : new IntegrationListItem { Id = id });

        public Task<IntegrationInput?> GetInputAsync(int id, CancellationToken cancellationToken) => Task.FromResult<IntegrationInput?>(Save.NotFound ? null : new IntegrationInput());

        public Task<IReadOnlyList<ConnectorOption>> ListConnectorsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ConnectorOption>>([]);

        public Task<IReadOnlyList<PayloadFormatOption>> ListPayloadFormatOptionsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PayloadFormatOption>>([]);

        public Task<IReadOnlyList<DeviceTypeOption>> ListDeviceTypeOptionsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DeviceTypeOption>>([]);

        public Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EmployeeOption>>([]);

        public Task<ApiSaveResult> CreateAsync(IntegrationInput input, CancellationToken cancellationToken) => Record("create");

        public Task<ApiSaveResult> UpdateAsync(int id, IntegrationInput input, CancellationToken cancellationToken) => Record("update");

        public Task<IntegrationHealthReport?> GetHealthAsync(int id, CancellationToken cancellationToken) => Task.FromResult<IntegrationHealthReport?>(null);

        public Task<IReadOnlyList<DeviceItem>> ListDevicesAsync(int integrationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DeviceItem>>([]);

        public Task<IReadOnlyList<DeviceItem>> ListAllDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DeviceItem>>(Devices);

        public Task<DeviceItem?> GetDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken) =>
            Task.FromResult<DeviceItem?>(Save.NotFound ? null : new DeviceItem { Id = deviceId, IntegrationId = integrationId, IntegrationName = "HQ", Name = "Front door", ExternalDeviceId = "1", TimeZoneId = "Asia/Beirut" });

        public Task<ApiSaveResult> AddDeviceAsync(int integrationId, DeviceInput input, CancellationToken cancellationToken) => Record("addDevice");

        public Task<ApiSaveResult> UpdateDeviceAsync(int integrationId, int deviceId, DeviceInput input, CancellationToken cancellationToken) => Record("updateDevice");

        public Task<ApiSaveResult> DeleteDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken) => Record("deleteDevice");

        public Task<IReadOnlyList<MappingItem>> ListMappingsAsync(int integrationId, int deviceId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MappingItem>>([]);

        public Task<ApiSaveResult> AddMappingAsync(int integrationId, int deviceId, MappingInput input, CancellationToken cancellationToken) => Record("addMapping");

        public Task<ApiSaveResult> DeleteMappingAsync(int integrationId, int deviceId, int mappingId, CancellationToken cancellationToken) => Record("deleteMapping");

        public Task<IReadOnlyList<RawNormalizedMappingItem>?> ListRawMappingAsync(int integrationId, int? batchId, string? status, int take, CancellationToken cancellationToken)
        {
            RawMappingTakes.Add(take);
            return Task.FromResult<IReadOnlyList<RawNormalizedMappingItem>?>(Save.NotFound ? null : []);
        }

        public Task<VendorMappingGuide?> GetRawMappingGuideAsync(int integrationId, CancellationToken cancellationToken) =>
            Task.FromResult<VendorMappingGuide?>(Save.NotFound ? null : new VendorMappingGuide { VendorKey = "zkteco" });

        public Task<SyncOutcomeResponse> RunAsync(int integrationId, bool reconcile, CancellationToken cancellationToken) => Task.FromResult(Outcome);

        public Task<RetryFailedResponse?> RetryFailedAsync(int integrationId, CancellationToken cancellationToken)
        {
            Calls.Add("retryFailed");
            return Task.FromResult<RetryFailedResponse?>(Save.NotFound ? null : new RetryFailedResponse { Requeued = 3 });
        }

        public Task<RenormalizeFlaggedResponse?> RenormalizeFlaggedAsync(int integrationId, CancellationToken cancellationToken)
        {
            Calls.Add("renormalizeFlagged");
            return Task.FromResult<RenormalizeFlaggedResponse?>(Save.NotFound ? null : Renormalized);
        }

        private Task<ApiSaveResult> Record(string call)
        {
            Calls.Add(call);
            return Task.FromResult(Save);
        }
    }
}
