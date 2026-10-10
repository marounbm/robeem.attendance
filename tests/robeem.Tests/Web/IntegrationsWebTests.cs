using System.Net;
using System.Text;
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
        var list = new StubHttpHandler(HttpStatusCode.OK, """[{"id":1,"displayName":"HQ","providerName":"Generic CSV","deviceCount":2,"mappingCount":5}]""");
        var missing = new IntegrationsApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient());

        var items = await new IntegrationsApiClient(list.CreateClient()).ListAsync(CancellationToken.None);

        Assert.Equal((2, 5), (items[0].DeviceCount, items[0].MappingCount));
        Assert.Equal("/api/attendance/integrations", list.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Null(await missing.GetAsync(9, CancellationToken.None));
        Assert.Null(await missing.GetInputAsync(9, CancellationToken.None));
        Assert.Null(await missing.GetHealthAsync(9, CancellationToken.None));
    }

    [Theory]
    [InlineData("providers", "/api/attendance/integrations/providers")]
    [InlineData("employees", "/api/attendance/integrations/employee-options")]
    public async Task TheClient_AsksTheRightPathForTheOptions(string which, string path)
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, "[]");
        var client = new IntegrationsApiClient(handler.CreateClient());

        if (which == "providers")
        {
            await client.ListProvidersAsync(CancellationToken.None);
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
        Assert.True((await gone.DeleteMappingAsync(1, 2, CancellationToken.None)).NotFound);
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
        await client.AddMappingAsync(5, new MappingInput(), CancellationToken.None);

        Assert.Equal(
            [("POST", "/api/attendance/integrations/5/sync"), ("POST", "/api/attendance/integrations/5/reconcile"), ("PUT", "/api/attendance/integrations/5/devices/8"), ("POST", "/api/attendance/integrations/5/mappings")],
            handler.Requests.Select(r => (r.Method.Method, r.RequestUri!.PathAndQuery)));
    }

    [Fact]
    public async Task Upload_SendsTheFileAsAMultipartPart_AndReturnsTheBatch()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """{"batchId":9,"status":"Succeeded","recordsIngested":5,"recordsFailed":1}""");
        var client = new IntegrationsApiClient(handler.CreateClient());
        await using var file = new MemoryStream(Encoding.UTF8.GetBytes("a,b\r\n1,2\r\n"));

        var result = await client.UploadAsync(5, file, "punches.csv", CancellationToken.None);

        Assert.Equal("/api/attendance/ingestion/5/uploads", handler.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Contains("name=file", handler.Bodies.Single());
        Assert.Contains("filename=punches.csv", handler.Bodies.Single());
        Assert.Contains("a,b", handler.Bodies.Single());
        Assert.Equal((5, 1), (result.Value!.RecordsIngested, result.Value.RecordsFailed));
    }

    [Fact]
    public async Task AnUploadTheApiRefuses_ComesBackAsAMessage()
    {
        var problem = """{"title":"x","status":400,"errors":{"file":["The file is empty."]}}""";
        var client = new IntegrationsApiClient(new StubHttpHandler(HttpStatusCode.BadRequest, problem, "application/problem+json").CreateClient());

        var result = await client.UploadAsync(5, new MemoryStream(), "empty.csv", CancellationToken.None);

        Assert.Equal((false, "The file is empty."), (result.Succeeded, result.MessageText));
    }

    [Fact]
    public async Task Creating_RedirectsToTheNewIntegration_OrShowsTheFormWithItsErrors()
    {
        var integrations = new FakeIntegrations { Save = ApiSaveResult.Success(7) };
        var controller = WithTempData(new IntegrationsController(integrations));

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
        var controller = WithTempData(new IntegrationsController(new FakeIntegrations { Save = ApiSaveResult.Missing() }));

        Assert.IsType<NotFoundResult>(await controller.Edit(9, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Details(9, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Edit(9, new IntegrationFormViewModel(), CancellationToken.None));
    }

    [Fact]
    public async Task ADeviceAction_ReportsItsOutcome_AndABadFormIsNeverSent()
    {
        var integrations = new FakeIntegrations { Save = ApiSaveResult.Success(1) };
        var controller = WithTempData(new IntegrationsController(integrations));

        await controller.AddDevice(5, new DeviceInput { Name = "Front door" }, CancellationToken.None);
        Assert.Equal("Device 'Front door' was added.", controller.TempData["StatusMessage"]);

        integrations.Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "This device has punches on record." });
        await controller.DeleteDevice(5, 2, CancellationToken.None);
        Assert.Equal("This device has punches on record.", controller.TempData["ErrorMessage"]);

        controller.ModelState.AddModelError("Name", "required");
        var calls = integrations.Calls.Count;
        await controller.AddDevice(5, new DeviceInput(), CancellationToken.None);
        Assert.Equal(calls, integrations.Calls.Count);
        Assert.Equal("Fill in every required field.", controller.TempData["ErrorMessage"]);
    }

    [Theory]
    [InlineData("Succeeded", "StatusMessage")]
    [InlineData("Skipped", "StatusMessage")]
    [InlineData("Failed", "ErrorMessage")]
    [InlineData("CircuitOpen", "ErrorMessage")]
    public async Task RunNow_ShowsAFailureAsAnError_AndTheRestAsInformation(string status, string key)
    {
        var integrations = new FakeIntegrations { Outcome = new SyncOutcomeResponse { Status = status, RecordsIngested = 3, Message = "note" } };
        var controller = WithTempData(new IntegrationsController(integrations));

        await controller.Run(5, reconcile: true, CancellationToken.None);

        Assert.Contains($"Reconciliation: {status}, 3 record(s). note", (string)controller.TempData[key]!);
    }

    [Fact]
    public async Task Upload_RefusesNothing_AnEmptyFile_AndReportsABatchThatFailed()
    {
        var integrations = new FakeIntegrations();
        var controller = WithTempData(new IntegrationsController(integrations));

        await controller.Upload(5, null, CancellationToken.None);
        Assert.Contains("can't be empty", (string)controller.TempData["ErrorMessage"]!);
        Assert.Empty(integrations.Uploads);

        integrations.Batch = new AttendanceIngestionResult { Status = "PartiallyFailed", RecordsIngested = 5, RecordsFailed = 1 };
        await controller.Upload(5, FormFile("punches.csv", "a,b\r\n"), CancellationToken.None);
        Assert.Equal("punches.csv", Assert.Single(integrations.Uploads));
        Assert.Contains("5 row(s) stored, 1 unusable", (string)controller.TempData["StatusMessage"]!);

        integrations.Batch = new AttendanceIngestionResult { Status = "Failed", ErrorSummary = "Could not read the file." };
        await controller.Upload(5, FormFile("bad.csv", "x"), CancellationToken.None);
        Assert.Contains("Could not read the file.", (string)controller.TempData["ErrorMessage"]!);
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
        var controller = WithTempData(new IntegrationsController(integrations));

        var page = await controller.RawMapping(5, null, null, 100000, CancellationToken.None);
        await controller.RawMapping(5, null, null, 0, CancellationToken.None);

        Assert.IsType<ViewResult>(page);
        Assert.Equal([500, 100], integrations.RawMappingTakes);

        integrations.Save = ApiSaveResult.Missing();
        Assert.IsType<NotFoundResult>(await controller.RawMapping(9, null, null, 100, CancellationToken.None));
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

    private static FormFile FormFile(string name, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);
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

    private sealed class FakeIntegrations : IIntegrationsApiClient
    {
        public ApiSaveResult Save { get; set; } = ApiSaveResult.Success(1);

        public SyncOutcomeResponse Outcome { get; set; } = new() { Status = "Succeeded" };

        public AttendanceIngestionResult Batch { get; set; } = new() { Status = "Succeeded" };

        public List<string> Calls { get; } = [];

        public List<string> Uploads { get; } = [];

        public List<int> RawMappingTakes { get; } = [];

        public Task<IReadOnlyList<IntegrationListItem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<IntegrationListItem>>([]);

        public Task<IntegrationListItem?> GetAsync(int id, CancellationToken cancellationToken) => Task.FromResult<IntegrationListItem?>(Save.NotFound ? null : new IntegrationListItem { Id = id });

        public Task<IntegrationInput?> GetInputAsync(int id, CancellationToken cancellationToken) => Task.FromResult<IntegrationInput?>(Save.NotFound ? null : new IntegrationInput());

        public Task<IReadOnlyList<ProviderOption>> ListProvidersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderOption>>([]);

        public Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EmployeeOption>>([]);

        public Task<ApiSaveResult> CreateAsync(IntegrationInput input, CancellationToken cancellationToken) => Record("create");

        public Task<ApiSaveResult> UpdateAsync(int id, IntegrationInput input, CancellationToken cancellationToken) => Record("update");

        public Task<IntegrationHealthReport?> GetHealthAsync(int id, CancellationToken cancellationToken) => Task.FromResult<IntegrationHealthReport?>(null);

        public Task<IReadOnlyList<DeviceItem>> ListDevicesAsync(int integrationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DeviceItem>>([]);

        public Task<ApiSaveResult> AddDeviceAsync(int integrationId, DeviceInput input, CancellationToken cancellationToken) => Record("addDevice");

        public Task<ApiSaveResult> UpdateDeviceAsync(int integrationId, int deviceId, DeviceInput input, CancellationToken cancellationToken) => Record("updateDevice");

        public Task<ApiSaveResult> DeleteDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken) => Record("deleteDevice");

        public Task<IReadOnlyList<MappingItem>> ListMappingsAsync(int integrationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MappingItem>>([]);

        public Task<ApiSaveResult> AddMappingAsync(int integrationId, MappingInput input, CancellationToken cancellationToken) => Record("addMapping");

        public Task<ApiSaveResult> DeleteMappingAsync(int integrationId, int mappingId, CancellationToken cancellationToken) => Record("deleteMapping");

        public Task<IReadOnlyList<RawNormalizedMappingItem>?> ListRawMappingAsync(int integrationId, int? batchId, string? status, int take, CancellationToken cancellationToken)
        {
            RawMappingTakes.Add(take);
            return Task.FromResult<IReadOnlyList<RawNormalizedMappingItem>?>(Save.NotFound ? null : []);
        }

        public Task<ProviderMappingGuide?> GetRawMappingGuideAsync(int integrationId, CancellationToken cancellationToken) =>
            Task.FromResult<ProviderMappingGuide?>(Save.NotFound ? null : new ProviderMappingGuide { ProviderKey = "zkteco" });

        public Task<SyncOutcomeResponse> RunAsync(int integrationId, bool reconcile, CancellationToken cancellationToken) => Task.FromResult(Outcome);

        public Task<ApiCommandResult<AttendanceIngestionResult>> UploadAsync(int integrationId, Stream file, string fileName, CancellationToken cancellationToken)
        {
            Uploads.Add(fileName);
            return Task.FromResult(ApiCommandResult<AttendanceIngestionResult>.Ok(Batch));
        }

        private Task<ApiSaveResult> Record(string call)
        {
            Calls.Add(call);
            return Task.FromResult(Save);
        }
    }
}
