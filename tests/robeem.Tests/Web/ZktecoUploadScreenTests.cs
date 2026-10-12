using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Attendance.Seeding;
using robeem.Application.Persistence;
using robeem.Application.Seeding;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Seeding;
using robeem.Tests.Api;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;

namespace robeem.Tests.Web;

/// <summary>
/// The whole upload path for the real ZKTeco export, the way a browser takes it: the Web controller,
/// then HTTP multipart to the Api controller, then the real ingestion service and the ZKTeco reader,
/// on a database seeded with the real catalog.
/// </summary>
public sealed class ZktecoUploadScreenTests : IAsyncDisposable
{
    private const string FileName = "Attendance data April and May 2026.xls";

    private readonly TestDatabase _database = new();
    private readonly TestApi _api;
    private readonly int _integrationId;

    public ZktecoUploadScreenTests()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            new SeedDataRunner(db, new ISeedContributor[] { new AttendanceSeedContributor() }, NullLogger<SeedDataRunner>.Instance)
                .RunAsync(CancellationToken.None).GetAwaiter().GetResult();

            var integration = new AttendanceIntegration
            {
                Provider = db.AttendanceProviders.Single(p => p.ProviderKey == ZktecoProvider.ProviderKey),
                DisplayName = "Head office U350",
            };
            db.Add(new AttendanceDevice { Integration = integration, ExternalDeviceId = "1", Name = "U350", TimeZoneId = "Asia/Beirut" });
            db.SaveChanges();
            _integrationId = integration.Id;
        }

        _api = TestApi.Start(services =>
        {
            services.AddKeyedSingleton<IAttendanceProvider, ZktecoProvider>(ZktecoProvider.ProviderKey);
            services.AddKeyedSingleton<IAttendanceEventInterpreter, ZktecoInterpreter>(ZktecoProvider.ProviderKey);
            services.AddSingleton<IRawPayloadSanitizer>(new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())));
            services.AddScoped<IRobeemDbContext>(_ => _database.CreateContext(TestDatabase.TenantA));
            services.AddScoped<IIngestionService, IngestionService>();
        });
        _api.Client.DefaultRequestHeaders.Add("X-Test-User", TestApi.As(1, AttendanceRoles.HRAdministrator));
    }

    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    public async Task TheScreen_OffersTheZktecoIntegration_WithTheXlsFormatAndItsLayout()
    {
        var controller = WithTempData(new UploadsController(new UploadsApiClient(_api.Client)));

        var model = (UploadViewModel)((ViewResult)await controller.Index(_integrationId, CancellationToken.None)).Model!;

        var target = Assert.IsType<robeem.Contracts.Attendance.UploadTarget>(model.Selected);
        Assert.Equal([".xls", ".xlsx", ".csv"], target.FileFormats);
        Assert.Contains(target.Layout.Fields, f => f.Source == "Date/Time");
        Assert.Contains(target.Layout.Fields, f => f.Source == "No.");
    }

    [Fact]
    public async Task TheRealExport_IsUploadedThroughTheScreen_AndEveryRowIsUsable()
    {
        var controller = WithTempData(new UploadsController(new UploadsApiClient(_api.Client)));
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "ZktecoExport.xls"));
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", FileName);

        await controller.Upload(_integrationId, file, CancellationToken.None);

        Assert.False(controller.TempData.ContainsKey("ErrorMessage"), (string?)controller.TempData["ErrorMessage"]);
        var message = (string)controller.TempData["StatusMessage"]!;
        Assert.StartsWith($"Uploaded {FileName}: 369 row(s) stored, 0 unusable. Status: Succeeded.", message);
        Assert.Contains($"SHA-256 {Convert.ToHexStringLower(global::System.Security.Cryptography.SHA256.HashData(bytes))}", message);
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
}
