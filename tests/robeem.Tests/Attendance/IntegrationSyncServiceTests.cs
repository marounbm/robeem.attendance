using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>Scheduled sync (P10): the cursor moves only after the rows are committed, failures are recorded and trip the breaker, and overlap is absorbed.</summary>
public sealed class IntegrationSyncServiceTests : AttendanceEngineTestBase
{
    private static readonly SyncCursor StartCursor = new("cursor-1");

    private readonly ScriptedProvider _provider = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly ServiceProvider _adapters;
    private readonly ServiceProvider _interpreters = new ServiceCollection()
        .AddKeyedSingleton<IAttendanceEventInterpreter, GenericWebhookInterpreter>("generic-csv")
        .BuildServiceProvider();

    private readonly AttendanceSyncOptions _options = new()
    {
        RetryAttempts = 2,
        RetryDelaySeconds = 0,
        BreakerFailureThreshold = 2,
        OverlapMinutes = 5,
    };

    private readonly IntegrationResilience _resilience;
    private readonly int _integrationId;

    public IntegrationSyncServiceTests()
    {
        _adapters = new ServiceCollection().AddKeyedSingleton<IAttendanceAdapter>("generic-csv", _provider).BuildServiceProvider();
        _resilience = new IntegrationResilience(Options.Create(_options), Clock);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        integration.SyncCursor = StartCursor.Value;
        db.AttendanceConnections.Add(new AttendanceConnection { IntegrationId = integration.Id, Name = "Default", IsDefault = true, Transport = AttendanceTransport.RestApi, AuthMethod = ConnectionAuthMethod.SessionLogin, PollingSchedule = "*/5 * * * *" });
        db.ExternalEmployeeIdMappings.Add(new ExternalEmployeeIdMapping { DeviceId = db.AttendanceDevices.Single(d => d.IntegrationId == integration.Id).Id, ExternalEmployeeId = "E-1", EmployeeId = EmployeeId });
        db.SaveChanges();
        _integrationId = integration.Id;
    }

    [Fact]
    public async Task SuccessfulSync_StoresTheRows_AndAdvancesTheCursor()
    {
        Clock.Local = Monday.At(9, 30);
        _provider.Changes = (_, _) => new AttendanceBatchResult([Punch("T1", "2026-10-05 08:00:00", "IN")], new SyncCursor("cursor-2"));

        var outcome = await SyncAsync();

        Assert.Equal(SyncOutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal(1, outcome.RecordsIngested);

        // The adapter got the stored cursor and the configured overlap.
        Assert.Equal(StartCursor, _provider.LastCursor);
        Assert.Equal(TimeSpan.FromMinutes(5), _provider.LastConfig!.SyncOverlap);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        Assert.Equal("cursor-2", integration.SyncCursor);
        Assert.Equal(Monday.At(9, 30), integration.LastSuccessfulSyncAt);
        Assert.Equal(AttendanceIntegrationHealth.Healthy, integration.HealthStatus);

        var batch = db.AttendanceProcessingBatches.Single(b => b.BatchType == AttendanceBatchType.IncrementalSync);
        Assert.Equal((AttendanceBatchStatus.Succeeded, 1), (batch.Status, batch.RecordsIngested));
        Assert.Equal(batch.Id, db.RawAttendanceTransactions.Single(r => r.ExternalTransactionId == "T1").ImportBatchId);
    }

    [Fact]
    public async Task TheAdapterGetsTheSecretTheReferenceNames_ForTheCall_AndItNeverShowsInTheConfigsText()
    {
        _secrets.Set("vendor-key", "s3cret-value");
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceConnections.Single().SecretRef = "vendor-key";
            db.SaveChanges();
        }

        _provider.Changes = (_, _) => new AttendanceBatchResult([], new SyncCursor("cursor-2"));

        Assert.Equal(SyncOutcomeStatus.Succeeded, (await SyncAsync()).Status);

        Assert.Equal("s3cret-value", _provider.LastConfig!.ConnectionSecret);
        Assert.Equal("vendor-key", _provider.LastConfig.ConnectionConfigRef);
        Assert.DoesNotContain("s3cret-value", _provider.LastConfig.ToString());
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal("vendor-key", check.AttendanceConnections.Single().SecretRef);
    }

    [Fact]
    public async Task AReferenceThatNamesNoSecret_GivesTheAdapterNone()
    {
        _provider.Changes = (_, _) => new AttendanceBatchResult([], new SyncCursor("cursor-2"));

        await SyncAsync();

        Assert.Null(_provider.LastConfig!.ConnectionSecret);
    }

    [Fact]
    public async Task TheAdapterGetsTheConnectionsSettings_AndTheBatchRecordsTheConnection()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceConnections.Single().Settings = "{\"baseUrl\":\"https://biostar.example.test:8443\"}";
            db.SaveChanges();
        }

        _provider.Changes = (_, _) => new AttendanceBatchResult([Punch("T1", "2026-10-05 08:00:00", "IN")], new SyncCursor("cursor-2"));

        await SyncAsync();

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var connection = check.AttendanceConnections.Single();
        Assert.Equal("https://biostar.example.test:8443", _provider.LastConfig!.Settings["baseUrl"]);
        Assert.Equal(connection.Id, _provider.LastConfig.ConnectionId);
        Assert.Equal(connection.Id, check.AttendanceProcessingBatches.Single(b => b.BatchType == AttendanceBatchType.IncrementalSync).ConnectionId);
    }

    [Fact]
    public async Task ASwitchedOffDefaultConnection_IsNotPulledThrough_AndIsNotPolled()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceConnections.Single().IsActive = false;
            db.SaveChanges();
        }

        var outcome = await SyncAsync();

        Assert.Equal(SyncOutcomeStatus.Skipped, outcome.Status);
        Assert.Equal(0, _provider.ChangeCalls);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(await CreateService(check).ListPollingIntegrationsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AFailedSync_RecordsTheConnectionOnItsFailedBatchToo()
    {
        _provider.Changes = (_, _) => throw new InvalidOperationException("down");

        await SyncAsync();

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(check.AttendanceConnections.Single().Id, check.AttendanceProcessingBatches.Single(b => b.Status == AttendanceBatchStatus.Failed).ConnectionId);
    }

    [Fact]
    public async Task ASyncedPayload_NeverKeepsABiometricTemplate()
    {
        // Invariant 10 on the scheduled path too: the vendor's payload is cleaned before it is stored.
        var punch = Punch("T1", "2026-10-05 08:00:00", "IN") with
        {
            RawPayload = "{\"transaction_id\":\"T1\",\"employee_id\":\"E-1\",\"fingerprint_template\":\"AAEC\",\"FaceTemplate\":\"xyz\"}",
        };
        _provider.Changes = (_, _) => new AttendanceBatchResult([punch], new SyncCursor("cursor-2"));

        Assert.Equal(SyncOutcomeStatus.Succeeded, (await SyncAsync()).Status);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var payload = db.RawAttendanceTransactions.Single(r => r.ExternalTransactionId == "T1").RawPayload;
        Assert.Contains("employee_id", payload);
        Assert.DoesNotContain("fingerprint", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("template", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyIncrement_AdvancesTheCursor_AndLeavesAnEmptySucceededBatch()
    {
        Clock.Local = Monday.At(9, 0);
        _provider.Changes = (_, _) => new AttendanceBatchResult([], new SyncCursor("cursor-2"));

        var outcome = await SyncAsync();

        Assert.Equal(SyncOutcomeStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.BatchId);
        Assert.Equal(0, outcome.RecordsIngested);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal("cursor-2", db.AttendanceIntegrations.Single().SyncCursor);

        // The quiet sync is on record, so the health report counts it as a success.
        var batch = db.AttendanceProcessingBatches.Single(b => b.BatchType == AttendanceBatchType.IncrementalSync);
        Assert.Equal((AttendanceBatchStatus.Succeeded, 0, Monday.At(9, 0), Monday.At(9, 0)), (batch.Status, batch.RecordsIngested, batch.StartedAt, batch.CompletedAt));
    }

    [Fact]
    public async Task AQuietIntegration_WithOneFailureInTwenty_ReportsAFivePercentFailureRate_NotFifty()
    {
        _provider.Changes = (_, _) => new AttendanceBatchResult([], new SyncCursor("cursor-2"));
        for (var i = 0; i < 19; i++)
        {
            Assert.Equal(SyncOutcomeStatus.Succeeded, (await SyncAsync()).Status);
        }

        _provider.Changes = (_, _) => throw new HttpRequestException("blip");
        Assert.Equal(SyncOutcomeStatus.Failed, (await SyncAsync()).Status);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var report = await CreateHealthService(db).GetIntegrationHealthAsync(_integrationId, CancellationToken.None);

        Assert.Equal((20, 1, 0.95), (report!.Syncs, report.FailedSyncs, report.SyncSuccessRate));
    }

    [Fact]
    public async Task Pruning_RemovesOnlyOldEmptySuccessfulSyncsAndReconciliations()
    {
        var old = Monday.AddDays(-10);
        int keptWithRowsId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var integration = db.AttendanceIntegrations.Single();
            AttendanceProcessingBatch Batch(AttendanceBatchType type, DateTime at, AttendanceBatchStatus status = AttendanceBatchStatus.Succeeded, int records = 0, string? error = null) =>
                new() { Integration = integration, BatchType = type, StartedAt = at, Status = status, RecordsIngested = records, ErrorSummary = error };

            var withRows = Batch(AttendanceBatchType.IncrementalSync, old, records: 1);
            db.AddRange(
                Batch(AttendanceBatchType.IncrementalSync, old),
                Batch(AttendanceBatchType.Reconciliation, old),
                Batch(AttendanceBatchType.IncrementalSync, Monday.AddDays(-1)),
                Batch(AttendanceBatchType.IncrementalSync, old, AttendanceBatchStatus.Failed, error: "vendor was down"),
                Batch(AttendanceBatchType.ManualUpload, old),
                withRows);
            db.RawAttendanceTransactions.Add(new RawAttendanceTransaction
            {
                Integration = integration,
                ImportBatch = withRows,
                TransactionTimestampRaw = "-",
                RawPayload = "{}",
                SourceSystem = "generic-csv",
                RecordHash = "kept-row",
            });
            db.SaveChanges();
            keptWithRowsId = withRows.Id;
        }

        int pruned;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            pruned = await CreateService(db).PruneEmptyBatchesAsync(CancellationToken.None);
        }

        // The two old empty successes are gone; the recent one, the failure, the upload and the one with rows stay.
        Assert.Equal(2, pruned);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        var remaining = check.AttendanceProcessingBatches.ToList();
        Assert.DoesNotContain(remaining, b => b.StartedAt == old && b.Status == AttendanceBatchStatus.Succeeded && b.RecordsIngested == 0 && b.BatchType != AttendanceBatchType.ManualUpload);
        Assert.Contains(remaining, b => b.Id == keptWithRowsId);
        Assert.Contains(remaining, b => b.Status == AttendanceBatchStatus.Failed && b.ErrorSummary == "vendor was down");
        Assert.Contains(remaining, b => b.BatchType == AttendanceBatchType.ManualUpload && b.StartedAt == old);
        Assert.Contains(remaining, b => b.StartedAt == Monday.AddDays(-1));
    }

    [Fact]
    public async Task Pruning_WithARetentionOfZero_KeepsEverything()
    {
        _options.EmptySyncRetentionDays = 0;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceProcessingBatches.Add(new AttendanceProcessingBatch { IntegrationId = _integrationId, BatchType = AttendanceBatchType.IncrementalSync, StartedAt = Monday.AddDays(-400), Status = AttendanceBatchStatus.Succeeded });
            db.SaveChanges();
        }

        using var run = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(0, await CreateService(run).PruneEmptyBatchesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FailedSync_LeavesTheCursorAlone_RecordsTheFailure_AndFlagsTheIntegration()
    {
        _provider.Changes = (_, _) => throw new HttpRequestException("vendor is down");

        var outcome = await SyncAsync();

        Assert.Equal(SyncOutcomeStatus.Failed, outcome.Status);
        Assert.Contains("vendor is down", outcome.Message);

        // One attempt, plus the configured retries.
        Assert.Equal(1 + _options.RetryAttempts, _provider.ChangeCalls);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        Assert.Equal(StartCursor.Value, integration.SyncCursor);
        Assert.Null(integration.LastSuccessfulSyncAt);
        Assert.Equal(AttendanceIntegrationHealth.Degraded, integration.HealthStatus);

        var batch = db.AttendanceProcessingBatches.Single(b => b.BatchType == AttendanceBatchType.IncrementalSync);
        Assert.Equal(AttendanceBatchStatus.Failed, batch.Status);
        Assert.Contains("vendor is down", batch.ErrorSummary);
        Assert.Empty(db.RawAttendanceTransactions.Where(r => r.ExternalTransactionId != null));
    }

    [Fact]
    public async Task TransientFailure_IsRetriedWithinTheSameSync()
    {
        var calls = 0;
        _provider.Changes = (_, _) => ++calls < 3
            ? throw new HttpRequestException("blip")
            : new AttendanceBatchResult([Punch("T1", "2026-10-05 08:00:00", "IN")], new SyncCursor("cursor-2"));

        var outcome = await SyncAsync();

        Assert.Equal(SyncOutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal(3, _provider.ChangeCalls);
    }

    [Fact]
    public async Task FailuresInARow_OpenTheBreaker_ThatThenShortCircuitsTheProvider()
    {
        _provider.Changes = (_, _) => throw new HttpRequestException("vendor is down");

        Assert.Equal(SyncOutcomeStatus.Failed, (await SyncAsync()).Status);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(AttendanceIntegrationHealth.Degraded, db.AttendanceIntegrations.Single().HealthStatus);
        }

        // The second failed sync reaches the threshold: the breaker opens and the integration is Unhealthy.
        Assert.Equal(SyncOutcomeStatus.Failed, (await SyncAsync()).Status);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(AttendanceIntegrationHealth.Unhealthy, db.AttendanceIntegrations.Single().HealthStatus);
        }

        var callsBefore = _provider.ChangeCalls;
        var outcome = await SyncAsync();

        Assert.Equal(SyncOutcomeStatus.CircuitOpen, outcome.Status);
        Assert.Equal(callsBefore, _provider.ChangeCalls);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(StartCursor.Value, check.AttendanceIntegrations.Single().SyncCursor);
    }

    [Fact]
    public async Task HealthReport_ReflectsTheTrippedBreaker()
    {
        _provider.Changes = (_, _) => throw new HttpRequestException("vendor is down");
        using (var before = Database.CreateContext(TestDatabase.TenantA))
        {
            var healthy = await CreateHealthService(before).GetIntegrationHealthAsync(_integrationId, CancellationToken.None);
            Assert.False(healthy!.IsCircuitOpen);
            Assert.Equal(nameof(AttendanceIntegrationHealth.Unknown), healthy.HealthStatus);
            Assert.Null(healthy.SyncSuccessRate);
        }

        await SyncAsync();
        await SyncAsync();

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var report = await CreateHealthService(db).GetIntegrationHealthAsync(_integrationId, CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal(nameof(AttendanceIntegrationHealth.Unhealthy), report.HealthStatus);
        Assert.True(report.IsCircuitOpen);
        Assert.Equal((2, 2, 0.0), (report.Syncs, report.FailedSyncs, report.SyncSuccessRate));
        Assert.Contains("vendor is down", report.LastFailureMessage);
        Assert.NotNull(report.LastFailureAt);
        Assert.Null(report.LastSuccessfulSyncAt);
    }

    [Fact]
    public async Task HealthReport_ForAnUnknownIntegration_IsNull()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);

        Assert.Null(await CreateHealthService(db).GetIntegrationHealthAsync(_integrationId + 100, CancellationToken.None));
    }

    [Fact]
    public async Task ASuccessBetweenFailures_KeepsTheBreakerClosed_AndHealsTheIntegration()
    {
        _provider.Changes = (_, _) => throw new HttpRequestException("vendor is down");
        await SyncAsync();

        _provider.Changes = (_, _) => new AttendanceBatchResult([], new SyncCursor("cursor-2"));
        Assert.Equal(SyncOutcomeStatus.Succeeded, (await SyncAsync()).Status);

        _provider.Changes = (_, _) => throw new HttpRequestException("vendor is down");
        Assert.Equal(SyncOutcomeStatus.Failed, (await SyncAsync()).Status);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceIntegrationHealth.Degraded, db.AttendanceIntegrations.Single().HealthStatus);
    }

    [Fact]
    public async Task AFailureToStoreTheRows_DoesNotAdvanceTheCursor()
    {
        // The same transaction id twice in one batch violates a unique index at save time.
        _provider.Changes = (_, _) => new AttendanceBatchResult(
            [Punch("T1", "2026-10-05 08:00:00", "IN"), Punch("T1", "2026-10-05 08:00:00", "IN")],
            new SyncCursor("cursor-2"));
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX ix_test_raw_external ON \"RawAttendanceTransaction\" (\"ExternalTransactionId\")");
        }

        var outcome = await SyncAsync();

        Assert.Equal(SyncOutcomeStatus.Failed, outcome.Status);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(StartCursor.Value, check.AttendanceIntegrations.Single().SyncCursor);
        Assert.Empty(check.RawAttendanceTransactions.Where(r => r.ExternalTransactionId == "T1"));
    }

    [Fact]
    public async Task AnAdapterWithoutIncrementalSync_IsSkipped_NotCountedAsAFailure()
    {
        _provider.Changes = (_, _) => throw new NotSupportedException("manual upload only");

        var outcome = await SyncAsync();

        Assert.Equal(SyncOutcomeStatus.Skipped, outcome.Status);
        Assert.Equal(1, _provider.ChangeCalls);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceIntegrationHealth.Unknown, db.AttendanceIntegrations.Single().HealthStatus);
        Assert.DoesNotContain(db.AttendanceProcessingBatches, b => b.BatchType == AttendanceBatchType.IncrementalSync);
    }

    [Fact]
    public async Task InactiveIntegration_IsSkipped()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceIntegrations.Single().Status = AttendanceIntegrationStatus.Paused;
            db.SaveChanges();
        }

        Assert.Equal(SyncOutcomeStatus.Skipped, (await SyncAsync()).Status);
        Assert.Equal(0, _provider.ChangeCalls);
    }

    [Fact]
    public async Task PollingList_HasActiveIntegrationsWithACron_NeverSyncedFirst()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var provider = db.AttendanceConnectorDefinitions.Single();
            db.AttendanceIntegrations.AddRange(
                new AttendanceIntegration { Connector = provider, DisplayName = "No cron" }.WithConnection(AttendanceTransport.RestApi),
                new AttendanceIntegration { Connector = provider, DisplayName = "Off", Status = AttendanceIntegrationStatus.Paused }.WithConnection(AttendanceTransport.RestApi, schedule: "* * * * *"));
            db.AttendanceIntegrations.Single(i => i.Id == _integrationId).LastSuccessfulSyncAt = Monday;
            db.AttendanceIntegrations.Add(new AttendanceIntegration { Connector = provider, DisplayName = "Fresh" }.WithConnection(AttendanceTransport.RestApi, schedule: "* * * * *"));
            db.SaveChanges();
        }

        using var check = Database.CreateContext(TestDatabase.TenantA);
        var list = await CreateService(check).ListPollingIntegrationsAsync(CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Null(list[0].LastSuccessfulSyncAt);
        Assert.Equal(_integrationId, list[1].IntegrationId);
    }

    [Fact]
    public async Task Reconciliation_PullsTheWideWindow_AndLeavesTheCursorAlone()
    {
        Clock.Local = Monday.At(10, 0);
        DateRange? window = null;
        _provider.Window = (_, range) =>
        {
            window = range;
            return new AttendanceBatchResult([Punch("T1", "2026-10-05 08:00:00", "IN")]);
        };

        var outcome = await CreateAndRun(s => s.ReconcileAsync(_integrationId, CancellationToken.None));

        Assert.Equal(SyncOutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal(Monday.At(10, 0).AddHours(-48), window!.From.UtcDateTime);
        Assert.Equal(Monday.At(10, 0), window.To.UtcDateTime);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        Assert.Equal(StartCursor.Value, integration.SyncCursor);
        Assert.Null(integration.LastSuccessfulSyncAt);
        Assert.Equal(AttendanceBatchType.Reconciliation, db.AttendanceProcessingBatches.Single(b => b.Id == outcome.BatchId).BatchType);
    }

    [Fact]
    public async Task RetriedSyncRedeliveringOverlap_LandsAsDuplicates_AndCountsTheHoursOnce()
    {
        AssignSchedule(new ShiftSpec("08:00", "17:00"));
        Clock.Local = Monday.At(18, 0);
        var punches = new[] { Punch("T1", "2026-10-05 08:00:00", "IN"), Punch("T2", "2026-10-05 17:00:00", "OUT") };

        // The first sync delivers both punches; the next one reads back over them (the overlap) and delivers them again.
        _provider.Changes = (_, _) => new AttendanceBatchResult(punches, new SyncCursor("cursor-2"));
        Assert.Equal(SyncOutcomeStatus.Succeeded, (await SyncAsync()).Status);
        _provider.Changes = (_, _) => throw new HttpRequestException("blip");
        Assert.Equal(SyncOutcomeStatus.Failed, (await SyncAsync()).Status);
        _provider.Changes = (_, _) => new AttendanceBatchResult(punches, new SyncCursor("cursor-3"));
        Assert.Equal(SyncOutcomeStatus.Succeeded, (await SyncAsync()).Status);

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var normalization = new NormalizationService(
                db,
                new NormalizedTransactionRecorder(db),
                new AttendancePolicyResolver(db),
                _interpreters,
                NullLogger<NormalizationService>.Instance);
            foreach (var batchId in await normalization.ListBatchesWithPendingTransactionsAsync(CancellationToken.None))
            {
                await normalization.NormalizeBatchAsync(batchId, CancellationToken.None);
            }
        }

        await ProcessAsync(Monday);

        using var check = Database.CreateContext(TestDatabase.TenantA);

        // Invariant 1: all four deliveries are stored raw; invariant 4: normalization absorbs the repeat.
        Assert.Equal(4, check.RawAttendanceTransactions.Count(r => r.ExternalTransactionId != null));
        Assert.Equal(2, check.NormalizedAttendanceTransactions.Count(n => n.DuplicateStatus == DuplicateStatus.Unique));
        Assert.Equal(2, check.NormalizedAttendanceTransactions.Count(n => n.DuplicateStatus == DuplicateStatus.DuplicateConfirmed));

        var record = check.AttendanceRecords.Single(r => r.IsCurrent);
        Assert.Equal(TimeSpan.FromHours(9), record.WorkingHours);
        Assert.Equal(1, check.AttendanceRecords.Count());
        Assert.Equal(TimeSpan.FromHours(9), check.AttendanceDaySummaries.Single().TotalWorkingHours);
        Assert.Equal("cursor-3", check.AttendanceIntegrations.Single().SyncCursor);
    }

    private static ExternalAttendanceTransaction Punch(string id, string timestamp, string type) =>
        new("{\"transaction_id\":\"" + id + "\",\"employee_id\":\"E-1\",\"device_id\":\"DEV-1\"}", timestamp)
        {
            ExternalTransactionId = id,
            ExternalEmployeeId = "E-1",
            DeviceExternalId = "DEV-1",
            TransactionTypeRaw = type,
            Timestamp = new VendorTimestamp(DateTime.Parse(timestamp, global::System.Globalization.CultureInfo.InvariantCulture), null),
        };

    private Task<SyncOutcome> SyncAsync() => CreateAndRun(s => s.SyncAsync(_integrationId, CancellationToken.None));

    private async Task<SyncOutcome> CreateAndRun(Func<IntegrationSyncService, Task<SyncOutcome>> run)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await run(CreateService(db));
    }

    private IntegrationHealthService CreateHealthService(RobeemDbContext db) =>
        new(db, _resilience, Options.Create(_options), CreateAudit(db), Clock);

    private IntegrationSyncService CreateService(RobeemDbContext db) => new(
        db,
        new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())),
        _adapters,
        _resilience,
        Options.Create(_options),
        _secrets,
        Clock,
        NullLogger<IntegrationSyncService>.Instance);

    /// <summary>An adapter whose answers the test scripts, and which remembers how it was called.</summary>
    private sealed class ScriptedProvider : IAttendanceAdapter
    {
        public Func<SyncCursor?, AttendanceIntegrationConfig, AttendanceBatchResult> Changes { get; set; } = (_, _) => new AttendanceBatchResult([]);

        public Func<AttendanceIntegrationConfig, DateRange?, AttendanceBatchResult> Window { get; set; } = (_, _) => new AttendanceBatchResult([]);

        public int ChangeCalls { get; private set; }

        public SyncCursor? LastCursor { get; private set; }

        public AttendanceIntegrationConfig? LastConfig { get; private set; }

        public Task<AttendanceBatchResult> GetTransactionChangesAsync(AttendanceIntegrationConfig config, SyncCursor? cursor, CancellationToken cancellationToken)
        {
            ChangeCalls++;
            LastCursor = cursor;
            LastConfig = config;
            return Task.FromResult(Changes(cursor, config));
        }

        public Task<AttendanceBatchResult> RetrieveAttendanceTransactionsAsync(AttendanceIntegrationConfig config, DateRange? range, CancellationToken cancellationToken) =>
            Task.FromResult(Window(config, range));

        public Task<ConnectionValidationResult> ValidateConnectionAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AuthResult> AuthenticateAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExternalDeviceDto>> RetrieveDevicesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExternalEmployeeDto>> RetrieveEmployeesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
