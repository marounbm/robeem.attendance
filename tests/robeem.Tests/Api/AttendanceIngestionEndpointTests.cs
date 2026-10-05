using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

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
    public async Task AnIntegrationAdministrator_UploadsAFile_AndGetsTheBatchOutcome()
    {
        var response = await Upload(7, As(AttendanceRoles.IntegrationAdministrator), "punches.csv", Csv);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AttendanceIngestionResult>();
        Assert.Equal(("Succeeded", 42, 1), (result!.Status, result.BatchId, result.RecordsFailed));

        // The service got the integration and the file's bytes, from a file that existed ...
        var call = Assert.Single(_ingestion.Calls);
        Assert.Equal((7, Csv), (call.IntegrationId, call.Content));

        // ... and that temporary file is gone once the request is over.
        Assert.False(File.Exists(call.Path), "The uploaded file was left on disk.");
    }

    [Fact]
    public async Task ATemporaryFile_IsRemoved_EvenWhenTheServiceThrows()
    {
        _ingestion.Throw = true;

        // The test server has no exception middleware, so the failure reaches the caller as it would reach it.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Upload(7, As(AttendanceRoles.IntegrationAdministrator), "punches.csv", Csv));

        Assert.False(File.Exists(Assert.Single(_ingestion.Calls).Path), "The uploaded file was left on disk.");
    }

    [Fact]
    public async Task AnEmptyFile_IsRefusedAs400_AndNeverReachesTheService()
    {
        var response = await Upload(7, As(AttendanceRoles.IntegrationAdministrator), "empty.csv", string.Empty);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("file", problem!.Errors.Keys);
        Assert.Empty(_ingestion.Calls);
    }

    [Fact]
    public async Task AnUnknownIntegration_Is404()
    {
        _ingestion.Known = false;

        Assert.Equal(HttpStatusCode.NotFound, (await Upload(7, As(AttendanceRoles.IntegrationAdministrator), "punches.csv", Csv)).StatusCode);
    }

    [Fact]
    public async Task ARequestWithoutAFilePart_IsRefused()
    {
        var response = await _api.Send(HttpMethod.Post, "api/attendance/ingestion/7/uploads", As(AttendanceRoles.IntegrationAdministrator), new { not = "a file" });

        Assert.InRange((int)response.StatusCode, 400, 499);
        Assert.Empty(_ingestion.Calls);
    }

    [Theory]
    [InlineData(AttendanceRoles.Employee)]
    [InlineData(AttendanceRoles.Manager)]
    [InlineData(AttendanceRoles.HRAdministrator)]
    [InlineData(AttendanceRoles.PayrollAdministrator)]
    public async Task AnyoneButAnIntegrationAdministrator_IsRefused_WithoutTheFileBeingRead(string role)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(7, As(role), "punches.csv", Csv)).StatusCode);
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
