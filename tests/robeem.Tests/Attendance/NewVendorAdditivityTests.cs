using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>
/// CLAUDE.md invariant 9, shown instead of asserted (P14 gap 8): a new vendor is one adapter and one
/// interpreter, registered under its key, and nothing else changes. "Acme Clock" is a made-up
/// vendor that differs from the CSV one in every way a vendor can: day-first timestamps, numeric
/// event codes, and a payload with a biometric field. Its two classes live in this file; the sync,
/// normalization, processing, exception and payroll code run on it untouched.
/// </summary>
public sealed class NewVendorAdditivityTests : AttendanceEngineTestBase
{
    private const string VendorKey = "acme-clock";

    private readonly AcmeProvider _provider = new();
    private readonly ServiceProvider _vendors;
    private readonly IntegrationResilience _resilience;
    private readonly int _integrationId;

    public NewVendorAdditivityTests()
    {
        _vendors = new ServiceCollection()
            .AddKeyedSingleton<IAttendanceProvider>(VendorKey, _provider)
            .AddKeyedSingleton<IAttendanceEventInterpreter, AcmeInterpreter>(VendorKey)
            .BuildServiceProvider();
        _resilience = new IntegrationResilience(Options.Create(new AttendanceSyncOptions()), Clock);

        // Configuration, not code: the catalog row and the integration of the new vendor.
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        db.AttendanceProviders.Single().ProviderKey = VendorKey;
        db.RawAttendanceTransactions.Single().ProcessingStatus = RawTransactionProcessingStatus.Normalized;
        db.ExternalEmployeeIdMappings.Add(new ExternalEmployeeIdMapping { IntegrationId = integration.Id, ExternalEmployeeId = "A-77", EmployeeId = EmployeeId });
        db.SaveChanges();
        _integrationId = integration.Id;
        Clock.Local = Monday.At(18, 0);
    }

    [Fact]
    public async Task ADayFromANewVendor_RunsThroughSyncNormalizationAndProcessing_Unchanged()
    {
        AssignSchedule(StandardDay);

        var outcome = await SyncAsync();
        await NormalizeAsync();
        await ProcessAsync(Monday);

        Assert.Equal((SyncOutcomeStatus.Succeeded, 4), (outcome.Status, outcome.RecordsIngested));
        using var db = Database.CreateContext(TestDatabase.TenantA);

        // The vendor's own formats came out as the neutral model.
        var rows = db.NormalizedAttendanceTransactions.OrderBy(n => n.EventTimestampLocal).ToList();
        Assert.Equal(
            [NormalizedEventType.CheckIn, NormalizedEventType.BreakOut, NormalizedEventType.BreakIn, NormalizedEventType.CheckOut],
            rows.Select(r => r.NormalizedEventType));
        Assert.Equal(Monday.At(8, 0), rows[0].EventTimestampLocal);
        Assert.Equal(["1", "3", "4", "2"], rows.Select(r => r.OriginalEventType));
        Assert.All(rows, r => Assert.Equal((EventConfidenceStatus.Certain, DuplicateStatus.Unique), (r.ConfidenceStatus, r.DuplicateStatus)));

        // The same day as the CSV vendor's Scenario 1.
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((TimeSpan.FromHours(8), TimeSpan.FromHours(1), AttendanceRecordStatus.Approved), (record.WorkingHours, record.BreakDuration, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task TheInvariantsHoldForTheNewVendor_WithNoCodeOfItsOwn()
    {
        var outcome = await SyncAsync();

        // Invariant 10: the vendor's face template never reaches the database, whoever wrote the adapter.
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var payloads = db.RawAttendanceTransactions.Where(r => r.ExternalTransactionId != null).Select(r => r.RawPayload).ToList();
        Assert.Equal(4, payloads.Count);
        Assert.All(payloads, p => Assert.DoesNotContain("face", p, StringComparison.OrdinalIgnoreCase));
        Assert.All(payloads, p => Assert.Contains("badge", p));

        // Invariant 1 and 4: a re-delivery stores raw again, and normalization takes the duplicates out.
        Assert.Equal(SyncOutcomeStatus.Succeeded, outcome.Status);
        await SyncAsync();
        await NormalizeAsync();
        Assert.Equal(8, db.RawAttendanceTransactions.Count(r => r.ExternalTransactionId != null));
        Assert.Equal(4, db.NormalizedAttendanceTransactions.Count(n => n.DuplicateStatus == DuplicateStatus.Unique));
        Assert.Equal(4, db.NormalizedAttendanceTransactions.Count(n => n.DuplicateStatus == DuplicateStatus.DuplicateConfirmed));
    }

    [Fact]
    public async Task ACodeTheInterpreterDoesNotKnow_FailsTheRawRow_AndNothingIsGuessed()
    {
        _provider.Transactions = [AcmeProvider.Punch("X1", "05/10/2026 08:00", "9")];

        await SyncAsync();
        var result = await NormalizeAsync();

        Assert.Equal(new NormalizationResult(0, 0, 0, 1), result);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.NormalizedAttendanceTransactions);
        Assert.Contains($"for provider '{VendorKey}'", db.RawAttendanceTransactions.Single(r => r.ExternalTransactionId == "X1").ProcessingError);
    }

    private async Task<SyncOutcome> SyncAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = new IntegrationSyncService(
            db,
            new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())),
            _vendors,
            _resilience,
            Options.Create(new AttendanceSyncOptions()),
            new FakeSecretStore(),
            Clock,
            NullLogger<IntegrationSyncService>.Instance);
        return await service.SyncAsync(_integrationId, CancellationToken.None);
    }

    private async Task<NormalizationResult> NormalizeAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = new NormalizationService(db, new NormalizedTransactionRecorder(db), new AttendancePolicyResolver(db), _vendors, NullLogger<NormalizationService>.Instance);
        var total = new NormalizationResult(0, 0, 0, 0);
        foreach (var batchId in await service.ListBatchesWithPendingTransactionsAsync(CancellationToken.None))
        {
            var result = await service.NormalizeBatchAsync(batchId, CancellationToken.None);
            total = new NormalizationResult(total.Normalized + result.Normalized, total.Duplicates + result.Duplicates, total.Flagged + result.Flagged, total.Failed + result.Failed);
        }

        return total;
    }

    /// <summary>The vendor's adapter: it only picks fields out, and interprets nothing.</summary>
    private sealed class AcmeProvider : IAttendanceProvider
    {
        public IReadOnlyList<ExternalAttendanceTransaction> Transactions { get; set; } =
        [
            Punch("A1", "05/10/2026 08:00", "1"),
            Punch("A2", "05/10/2026 12:00", "3"),
            Punch("A3", "05/10/2026 13:00", "4"),
            Punch("A4", "05/10/2026 17:00", "2"),
        ];

        public static ExternalAttendanceTransaction Punch(string id, string timestamp, string code) =>
            new($"{{\"id\":\"{id}\",\"badge\":\"A-77\",\"time\":\"{timestamp}\",\"code\":\"{code}\",\"faceTemplate\":\"QUJD\"}}", timestamp)
            {
                ExternalTransactionId = id,
                ExternalEmployeeId = "A-77",
                DeviceExternalId = "DEV-1",
                TransactionTypeRaw = code,
                Timestamp = AcmeInterpreter.Parse(timestamp),
            };

        public Task<AttendanceBatchResult> GetTransactionChangesAsync(AttendanceIntegrationConfig config, SyncCursor? cursor, CancellationToken cancellationToken) =>
            Task.FromResult(new AttendanceBatchResult(Transactions, new SyncCursor("acme-cursor")));

        public Task<AttendanceBatchResult> RetrieveAttendanceTransactionsAsync(AttendanceIntegrationConfig config, DateRange? range, CancellationToken cancellationToken) =>
            Task.FromResult(new AttendanceBatchResult(Transactions));

        public Task<ConnectionValidationResult> ValidateConnectionAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) =>
            Task.FromResult(new ConnectionValidationResult(true));

        public Task<AuthResult> AuthenticateAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) =>
            Task.FromResult(new AuthResult(true));

        public Task<IReadOnlyList<ExternalDeviceDto>> RetrieveDevicesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExternalDeviceDto>>([]);

        public Task<IReadOnlyList<ExternalEmployeeDto>> RetrieveEmployeesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExternalEmployeeDto>>([]);
    }

    /// <summary>How Acme writes things: <c>dd/MM/yyyy HH:mm</c>, and 1 to 4 for the four events.</summary>
    private sealed class AcmeInterpreter : IAttendanceEventInterpreter
    {
        public static VendorTimestamp? Parse(string text) =>
            DateTime.TryParseExact(text, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? new VendorTimestamp(parsed, null)
                : null;

        public VendorTimestamp? ParseTimestamp(string transactionTimestampRaw) => Parse(transactionTimestampRaw);

        public NormalizedEventType? MapEventType(string? transactionTypeRaw, string? directionRaw) => transactionTypeRaw switch
        {
            "1" => NormalizedEventType.CheckIn,
            "2" => NormalizedEventType.CheckOut,
            "3" => NormalizedEventType.BreakOut,
            "4" => NormalizedEventType.BreakIn,
            _ => null,
        };
    }
}
