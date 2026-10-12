using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// Uploads the real export of the ZKTeco U350 (Fingerprint Attendance System 4.8.7), a bare BIFF
/// <c>.xls</c> stream with text cells only, exactly as the clock's software wrote it.
/// </summary>
public sealed class ZktecoXlsUploadTests : IDisposable
{
    private static readonly string[] EmployeeNumbers = ["21001", "22001", "22002", "43001"];

    private readonly string _xlsPath = Path.Combine(AppContext.BaseDirectory, "TestData", "ZktecoExport.xls");
    private readonly TestDatabase _database = new();
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddKeyedSingleton<IAttendanceProvider, ZktecoProvider>(ZktecoProvider.ProviderKey)
        .AddKeyedSingleton<IAttendanceEventInterpreter, ZktecoInterpreter>(ZktecoProvider.ProviderKey)
        .BuildServiceProvider();

    private readonly int _integrationId;

    public ZktecoXlsUploadTests()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var integration = new AttendanceIntegration
        {
            Provider = new AttendanceProvider { ProviderKey = ZktecoProvider.ProviderKey, DisplayName = "ZKTeco export" },
            DisplayName = "Head office clock",
        };
        db.Add(new AttendanceDevice { Integration = integration, ExternalDeviceId = "1", Name = "U350", TimeZoneId = "Asia/Beirut" });
        foreach (var number in EmployeeNumbers)
        {
            db.Add(new ExternalEmployeeIdMapping
            {
                Integration = integration,
                ExternalEmployeeId = number,
                Employee = new Profile { EmployeeNumber = number, FirstName = "E", LastName = number, Title = $"E {number}" },
            });
        }

        db.SaveChanges();
        _integrationId = integration.Id;
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task TheExport_IsReadAsExported_WithoutSavingItAsCsvFirst()
    {
        var result = await Ingest();

        Assert.True(result.ErrorSummary is null, result.ErrorSummary);
        Assert.Equal(369, result.RecordsIngested);
        Assert.Equal(0, result.RecordsFailed);
        Assert.Equal("Succeeded", result.Status);
    }

    [Fact]
    public async Task TheExport_KeepsTheVendorsFields_AndNeverCardNo()
    {
        await Ingest();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var rows = db.RawAttendanceTransactions.OrderBy(r => r.Id).ToList();

        var first = rows[0];
        Assert.Equal("22001", first.ExternalEmployeeId);
        Assert.Equal("1", first.DeviceExternalId);
        Assert.Equal("C/In", first.TransactionTypeRaw);
        Assert.Equal("01-Apr-26 08:33:25", first.TransactionTimestampRaw);
        Assert.Equal(EmployeeNumbers.Order(), rows.Select(r => r.ExternalEmployeeId!).Distinct().Order());

        var payload = JsonDocument.Parse(first.RawPayload).RootElement;
        Assert.DoesNotContain(payload.EnumerateObject(), p => p.Name == "CardNo");
        Assert.Equal(8, payload.EnumerateObject().Count());
    }

    [Fact]
    public async Task TheExport_NormalizesEveryPunch_ThroughTheMappedEmployeesAndTheDevice()
    {
        var batchId = (await Ingest()).BatchId;

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var normalization = new NormalizationService(
                db,
                new NormalizedTransactionRecorder(db),
                new AttendancePolicyResolver(db),
                _services,
                NullLogger<NormalizationService>.Instance);
            await normalization.NormalizeBatchAsync(batchId, CancellationToken.None);
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.RawAttendanceTransactions.Where(r => r.ProcessingStatus == RawTransactionProcessingStatus.Failed));

        var normalized = check.NormalizedAttendanceTransactions.OrderBy(n => n.RawTransactionId).ToList();
        Assert.Equal(369, normalized.Count);
        Assert.All(normalized, n => Assert.NotNull(n.EmployeeId));
        Assert.All(normalized, n => Assert.Contains(n.NormalizedEventType, new[] { NormalizedEventType.CheckIn, NormalizedEventType.CheckOut }));
        Assert.Equal(new DateTime(2026, 4, 1, 8, 33, 25), normalized[0].EventTimestampLocal);
    }

    private async Task<robeem.Contracts.Attendance.AttendanceIngestionResult> Ingest()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var ingestion = new IngestionService(db, new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), _services);

        // The Api hands the adapter an extension-less temporary copy of the upload; do the same.
        var path = Path.GetTempFileName();
        try
        {
            File.Copy(_xlsPath, path, overwrite: true);
            return (await ingestion.IngestManualUploadAsync(_integrationId, path, CancellationToken.None))!;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
