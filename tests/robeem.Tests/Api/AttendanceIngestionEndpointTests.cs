using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Tests.Api;

/// <summary>
/// The manual upload endpoint over HTTP (P14 gap 5): a multipart file in, the ingestion result out.
/// The upload size limit is Kestrel's, which the in-memory server does not enforce, so it is not
/// tested here.
/// </summary>
public sealed class AttendanceIngestionEndpointTests : IAsyncDisposable
{
    private const string Csv = "transaction_id,employee_id,timestamp,type\r\nT1,E-1,2026-10-05 08:00:00,IN\r\n";

    private readonly FakeIngestion _ingestion = new();
    private readonly TestApi _api;

    public AttendanceIngestionEndpointTests()
    {
        _api = TestApi.Start(services => services.AddSingleton<IIngestionService>(_ingestion));
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task AnHrAdministrator_UploadsAFile_AndGetsTheBatchOutcome()
    {
        var response = await Upload(7, As(AttendanceRoles.HRAdministrator), "punches.csv", Csv);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AttendanceIngestionResult>();
        Assert.Equal(("Succeeded", 42, 1), (result!.Status, result.BatchId, result.RecordsFailed));

        // The service got the integration and the file's bytes, from a file that existed ...
        var call = Assert.Single(_ingestion.Calls);
        Assert.Equal((7, Csv), (call.IntegrationId, call.Content));

        // ... and that temporary file is gone once the request is over.
        Assert.False(File.Exists(call.Path), "The uploaded file was left on disk.");

        // The file's own name and the signed-in employee are what is recorded with it.
        Assert.Equal(("punches.csv", (int?)1), Assert.Single(_ingestion.Uploads));
    }

    [Fact]
    public async Task TheUploadHistory_IsListedForTheHrAdministrator_AndRefusedToOthers()
    {
        var listed = await _api.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/attendance/ingestion/uploads") { Headers = { { "X-Test-User", As(AttendanceRoles.HRAdministrator) } } });
        var refused = await _api.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/attendance/ingestion/uploads") { Headers = { { "X-Test-User", As(AttendanceRoles.Manager) } } });

        var page = (await listed.Content.ReadFromJsonAsync<PagedResult<UploadedFileItem>>())!;
        Assert.Equal(("punches.csv", 41), (page.Items.Single().FileName, page.Total));
        Assert.Equal((null, 1, 0), _ingestion.HistoryAsked);

        // The page and the size asked for are passed on; a page before the first is the first.
        await _api.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/attendance/ingestion/uploads?integrationId=7&page=3&pageSize=10") { Headers = { { "X-Test-User", As(AttendanceRoles.HRAdministrator) } } });
        Assert.Equal((7, 3, 10), _ingestion.HistoryAsked);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task ATemporaryFile_IsRemoved_EvenWhenTheServiceThrows()
    {
        _ingestion.Throw = true;

        // The test server has no exception middleware, so the failure reaches the caller as it would reach it.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Upload(7, As(AttendanceRoles.HRAdministrator), "punches.csv", Csv));

        Assert.False(File.Exists(Assert.Single(_ingestion.Calls).Path), "The uploaded file was left on disk.");
    }

    [Fact]
    public async Task AnEmptyFile_IsRefusedAs400_AndNeverReachesTheService()
    {
        var response = await Upload(7, As(AttendanceRoles.HRAdministrator), "empty.csv", string.Empty);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("file", problem!.Errors.Keys);
        Assert.Empty(_ingestion.Calls);
    }

    [Fact]
    public async Task AnUnknownIntegration_Is404()
    {
        _ingestion.Known = false;

        Assert.Equal(HttpStatusCode.NotFound, (await Upload(7, As(AttendanceRoles.HRAdministrator), "punches.csv", Csv)).StatusCode);
    }

    [Fact]
    public async Task ARequestWithoutAFilePart_IsRefused()
    {
        var response = await _api.Send(HttpMethod.Post, "api/attendance/ingestion/7/uploads", As(AttendanceRoles.HRAdministrator), new { not = "a file" });

        Assert.InRange((int)response.StatusCode, 400, 499);
        Assert.Empty(_ingestion.Calls);
    }

    [Fact]
    public async Task AnIntegrationThatTakesNoUploads_Is404_AndTheFileIsNeverRead()
    {
        // Integration 8 is not in the targets: inactive, or a provider that reads no files.
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(8, As(AttendanceRoles.HRAdministrator), "punches.csv", Csv)).StatusCode);
        Assert.Empty(_ingestion.Calls);
    }

    [Fact]
    public async Task TheTargets_AreListedWithTheirFormatsAndLayout()
    {
        var response = await _api.Send(HttpMethod.Get, "api/attendance/ingestion/targets", As(AttendanceRoles.HRAdministrator));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var target = Assert.Single((await response.Content.ReadFromJsonAsync<List<UploadTarget>>())!);
        Assert.Equal(7, target.IntegrationId);
        Assert.Equal([".csv"], target.FileExtensions);
        Assert.Equal("ExternalEmployeeId", target.Layout.Fields.Single().Target);
    }

    [Theory]
    [InlineData(AttendanceRoles.Employee)]
    [InlineData(AttendanceRoles.Manager)]
    [InlineData(AttendanceRoles.IntegrationAdministrator)]
    [InlineData(AttendanceRoles.PayrollAdministrator)]
    public async Task AnyoneButAnHrAdministrator_IsRefused_WithoutTheFileBeingRead(string role)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(7, As(role), "punches.csv", Csv)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.Send(HttpMethod.Get, "api/attendance/ingestion/targets", As(role))).StatusCode);
        Assert.Empty(_ingestion.Calls);
    }

    [Fact]
    public async Task NobodySignedIn_Is401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(7, null, "punches.csv", Csv)).StatusCode);
        Assert.Empty(_ingestion.Calls);
    }

    private static string As(params string[] roles) => TestApi.As(1, roles);

    private Task<HttpResponseMessage> Upload(int integrationId, string? user, string fileName, string content) =>
        _api.SendFile($"api/attendance/ingestion/{integrationId}/uploads", user, fileName, Encoding.UTF8.GetBytes(content));

    private sealed class FakeIngestion : IIngestionService
    {
        public List<(int IntegrationId, string Path, string Content)> Calls { get; } = [];

        public bool Known { get; set; } = true;

        public bool Throw { get; set; }

        public List<(string FileName, int? UploadedById)> Uploads { get; } = [];

        public (int? IntegrationId, int Page, int PageSize)? HistoryAsked { get; private set; }

        public Task<IReadOnlyList<UploadTarget>> ListUploadTargetsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UploadTarget>>(
            [
                new UploadTarget
                {
                    IntegrationId = 7,
                    DisplayName = "Head office",
                    VendorKey = "generic-csv",
                    VendorName = "Generic CSV",
                    FileExtensions = [".csv"],
                    Layout = new VendorMappingGuide { VendorKey = "generic-csv", Fields = [new FieldMappingRule { Source = "employee_id", Target = "ExternalEmployeeId" }] },
                },
            ]);

        public Task<AttendanceIngestionResult?> IngestWebhookPayloadAsync(int integrationId, int? connectionId, string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AttendanceIngestionResult?> IngestUploadedFileAsync(int integrationId, string filePath, string fileName, int? uploadedById, CancellationToken cancellationToken)
        {
            Uploads.Add((fileName, uploadedById));
            return IngestManualUploadAsync(integrationId, filePath, cancellationToken);
        }

        public Task<PagedResult<UploadedFileItem>> ListUploadedFilesAsync(int? integrationId, int page, int pageSize, CancellationToken cancellationToken)
        {
            HistoryAsked = (integrationId, page, pageSize);
            return Task.FromResult(new PagedResult<UploadedFileItem>
            {
                Items = [new UploadedFileItem { Id = 1, IntegrationId = 7, FileName = "punches.csv", Sha256 = new string('a', 64) }],
                Total = 41,
                Page = page,
                PageSize = pageSize,
            });
        }

        public async Task<AttendanceIngestionResult?> IngestManualUploadAsync(int integrationId, string filePath, CancellationToken cancellationToken)
        {
            Calls.Add((integrationId, filePath, await File.ReadAllTextAsync(filePath, cancellationToken)));
            if (Throw)
            {
                throw new InvalidOperationException("ingestion failed");
            }

            return Known ? new AttendanceIngestionResult { BatchId = 42, Status = "Succeeded", RecordsIngested = 1, RecordsFailed = 1 } : null;
        }
    }
}
