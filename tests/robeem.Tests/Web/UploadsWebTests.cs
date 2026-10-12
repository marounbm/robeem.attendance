using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Contracts.Attendance;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Tests.Web;

/// <summary>The upload screen: its typed client, its controller, and who sees it in the menu.</summary>
public sealed class UploadsWebTests
{
    [Fact]
    public async Task TheClient_ListsTheTargets_WithTheirFormatsAndLayout()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """[{"integrationId":3,"displayName":"Head office","providerName":"ZKTeco","fileFormats":[".xls",".csv"],"layout":{"providerKey":"zkteco","summary":"s","fields":[{"source":"No.","target":"ExternalEmployeeId"}]}}]""");

        var targets = await new UploadsApiClient(handler.CreateClient()).ListTargetsAsync(CancellationToken.None);

        Assert.Equal("/api/attendance/ingestion/targets", handler.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal(3, targets[0].IntegrationId);
        Assert.Equal([".xls", ".csv"], targets[0].FileFormats);
        Assert.Equal("ExternalEmployeeId", targets[0].Layout.Fields.Single().Target);
    }

    [Fact]
    public async Task Upload_SendsTheFileAsAMultipartPart_AndReturnsTheBatch()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """{"batchId":9,"status":"Succeeded","recordsIngested":5,"recordsFailed":1}""");
        var client = new UploadsApiClient(handler.CreateClient());
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
        var client = new UploadsApiClient(new StubHttpHandler(HttpStatusCode.BadRequest, problem, "application/problem+json").CreateClient());

        var result = await client.UploadAsync(5, new MemoryStream(), "empty.csv", CancellationToken.None);

        Assert.Equal((false, "The file is empty."), (result.Succeeded, result.MessageText));
    }

    [Fact]
    public async Task TheScreen_ChoosesTheOnlyTarget_ButWaitsForAChoiceAmongSeveral()
    {
        var one = new FakeUploads { Targets = [Target(3, ".csv")] };
        var two = new FakeUploads { Targets = [Target(3, ".csv"), Target(4, ".xls")] };

        Assert.Equal(3, Model(await new UploadsController(one).Index(null, CancellationToken.None)).Selected!.IntegrationId);
        Assert.Null(Model(await new UploadsController(two).Index(null, CancellationToken.None)).Selected);
        Assert.Equal(4, Model(await new UploadsController(two).Index(4, CancellationToken.None)).Selected!.IntegrationId);
        Assert.Null(Model(await new UploadsController(two).Index(99, CancellationToken.None)).Selected);
    }

    [Fact]
    public async Task Upload_RefusesAnEmptyFile_AFormatTheProviderDoesNotRead_AndAnIntegrationThatTakesNoUploads()
    {
        var uploads = new FakeUploads { Targets = [Target(3, ".xls", ".csv")] };
        var controller = WithTempData(new UploadsController(uploads));

        await controller.Upload(3, null, CancellationToken.None);
        Assert.Contains("can't be empty", (string)controller.TempData["ErrorMessage"]!);

        await controller.Upload(3, FormFile("punches.pdf", "x"), CancellationToken.None);
        Assert.Contains("reads .xls, .csv files", (string)controller.TempData["ErrorMessage"]!);

        await controller.Upload(8, FormFile("punches.csv", "x"), CancellationToken.None);
        Assert.Contains("does not take file uploads", (string)controller.TempData["ErrorMessage"]!);

        Assert.Empty(uploads.Sent);
    }

    [Fact]
    public async Task Upload_AcceptsTheFormat_InAnyCase_AndReportsTheBatch()
    {
        var uploads = new FakeUploads { Targets = [Target(3, ".xls")] };
        var controller = WithTempData(new UploadsController(uploads));

        uploads.Batch = new AttendanceIngestionResult { Status = "PartiallyFailed", RecordsIngested = 5, RecordsFailed = 1 };
        await controller.Upload(3, FormFile("EXPORT.XLS", "x"), CancellationToken.None);
        Assert.Equal((3, "EXPORT.XLS"), Assert.Single(uploads.Sent));
        Assert.Contains("5 row(s) stored, 1 unusable", (string)controller.TempData["StatusMessage"]!);

        // The file's hash is reported, and a file seen before is called out.
        var hash = new string('c', 64);
        uploads.Batch = new AttendanceIngestionResult { Status = "Succeeded", RecordsIngested = 5, Sha256 = hash, SameFileUploadedAt = new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.Zero) };
        await controller.Upload(3, FormFile("repeat.xls", "x"), CancellationToken.None);
        var repeated = (string)controller.TempData["StatusMessage"]!;
        Assert.Contains($"SHA-256 {hash}", repeated);
        Assert.Contains("already uploaded on 2026-10-01 08:30 UTC", repeated);

        uploads.Batch = new AttendanceIngestionResult { Status = "Failed", ErrorSummary = "Could not read the file." };
        await controller.Upload(3, FormFile("again.xls", "x"), CancellationToken.None);
        Assert.Contains("Could not read the file.", (string)controller.TempData["ErrorMessage"]!);
        Assert.DoesNotContain("Every row failed", (string)controller.TempData["ErrorMessage"]!);

        // Every row unusable: the message points at the layout, not at the rows.
        uploads.Batch = new AttendanceIngestionResult { Status = "Failed", RecordsIngested = 369, RecordsFailed = 369, ErrorSummary = "For example: Unparseable timestamp 'x'." };
        await controller.Upload(3, FormFile("all-bad.xls", "x"), CancellationToken.None);
        var message = (string)controller.TempData["ErrorMessage"]!;
        Assert.Contains("369 row(s) stored, 369 unusable", message);
        Assert.Contains("Unparseable timestamp 'x'", message);
        Assert.Contains("not in the layout of Clock 3 (ZKTeco)", message);
    }

    [Fact]
    public void OnlyHrAdministrators_SeeTheUploadScreen()
    {
        var item = NavigationMenu.Find("Uploads")!;

        Assert.Equal([NavigationMenu.Hr], item.Roles);
        Assert.DoesNotContain("Uploads", Controllers("Employee", "Manager", "PayrollAdministrator", "IntegrationAdministrator"));
        Assert.Contains("Uploads", Controllers("HRAdministrator"));
        Assert.DoesNotContain("Upload", NavigationMenu.Find("Integrations")!.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Controllers(params string[] roles) =>
        NavigationMenu.For(new FakeActor(roles)).SelectMany(s => s.Items).Select(i => i.Controller);

    private static UploadViewModel Model(IActionResult result) => (UploadViewModel)((ViewResult)result).Model!;

    private static UploadTarget Target(int id, params string[] formats) => new()
    {
        IntegrationId = id,
        DisplayName = $"Clock {id}",
        ProviderName = "ZKTeco",
        FileFormats = formats,
        Layout = new ProviderMappingGuide { ProviderKey = "zkteco" },
    };

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

    private sealed class FakeActor : ICurrentActor
    {
        private readonly string[] _roles;

        public FakeActor(string[] roles)
        {
            _roles = roles;
        }

        public bool HasChoice => true;

        public ActingAs? Choice => new(1, "Test", _roles);

        public bool IsInRole(string role) => _roles.Contains(role);

        public bool IsInAnyRole(params string[] roles) => roles.Any(IsInRole);
    }

    private sealed class FakeUploads : IUploadsApiClient
    {
        public IReadOnlyList<UploadTarget> Targets { get; set; } = [];

        public AttendanceIngestionResult Batch { get; set; } = new() { Status = "Succeeded" };

        public List<(int IntegrationId, string FileName)> Sent { get; } = [];

        public Task<IReadOnlyList<UploadTarget>> ListTargetsAsync(CancellationToken cancellationToken) => Task.FromResult(Targets);

        public Task<IReadOnlyList<UploadedFileItem>> ListUploadsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UploadedFileItem>>([]);

        public Task<ApiCommandResult<AttendanceIngestionResult>> UploadAsync(int integrationId, Stream file, string fileName, CancellationToken cancellationToken)
        {
            Sent.Add((integrationId, fileName));
            return Task.FromResult(ApiCommandResult<AttendanceIngestionResult>.Ok(Batch));
        }
    }
}
