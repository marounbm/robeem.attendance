using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Api.Attendance;
using robeem.Api.Attendance.Jobs;
using robeem.Application.Attendance;
using robeem.Application.Tenancy;

namespace robeem.Tests.Api;

/// <summary>
/// The three scheduled jobs (P10): which tenants and integrations they touch, when an integration
/// is due, and that one failing never stops the rest. The services behind them are fakes; their own
/// logic is tested elsewhere.
/// </summary>
public sealed class AttendanceJobsTests
{
    private static readonly DateTime Monday = new(2026, 10, 5);

    private readonly Log _log = new();

    [Fact]
    public async Task Normalization_WorksEachTenantsBatches_InThatTenantsOwnScope()
    {
        var tenants = new FakeTenants(_log).Add("a", pendingBatches: [1, 2]).Add("b", pendingBatches: [7]);

        await NormalizationJob(tenants).RunAsync(CancellationToken.None);

        Assert.Equal(["a:normalize:1", "a:normalize:2", "b:normalize:7"], _log.Entries);
    }

    [Fact]
    public async Task Normalization_ATenantWhoseScopeCannotBeCreated_DoesNotStopTheOthers()
    {
        var tenants = new FakeTenants(_log).Add("a", pendingBatches: [1], broken: true).Add("b", pendingBatches: [7]);

        await NormalizationJob(tenants).RunAsync(CancellationToken.None);

        Assert.Equal(["b:normalize:7"], _log.Entries);
    }

    [Fact]
    public async Task Normalization_ABatchThatThrows_DoesNotStopTheOtherTenants()
    {
        var tenants = new FakeTenants(_log).Add("a", pendingBatches: [1], throwOnNormalize: true).Add("b", pendingBatches: [7]);

        await NormalizationJob(tenants).RunAsync(CancellationToken.None);

        Assert.Equal(["a:normalize:1", "b:normalize:7"], _log.Entries);
    }

    [Fact]
    public async Task Normalization_ShuttingDown_IsNotSwallowed()
    {
        var tenants = new FakeTenants(_log).Add("a", pendingBatches: [1], throwOnNormalize: true);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => NormalizationJob(tenants).RunAsync(cts.Token));
    }

    [Fact]
    public void Normalization_FollowsItsOptions()
    {
        var job = new NormalizationJob(new FakeTenants(_log), new FakeTenants(_log), Options.Create(new AttendanceNormalizationOptions { Enabled = false, PollIntervalSeconds = 0 }), NullLogger<NormalizationJob>.Instance);

        Assert.False(job.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(1), job.Schedule.Interval);
        Assert.Null(job.Schedule.Cron);
    }

    [Fact]
    public async Task Sync_AnIntegrationThatNeverSynced_IsDueAtOnce_AndThenWaitsForItsNextCronTime()
    {
        var clock = new FixedTimeProvider(Monday.At(10, 2));
        var tenants = new FakeTenants(_log).Add("a", polling: [new PollingIntegration(1, "*/5 * * * *", null)]);
        var job = SyncJob(tenants, clock);

        await job.RunAsync(CancellationToken.None);
        await job.RunAsync(CancellationToken.None);
        Assert.Equal(["a:sync:1"], _log.Entries);

        clock.Local = Monday.At(10, 4);
        await job.RunAsync(CancellationToken.None);
        Assert.Equal(["a:sync:1"], _log.Entries);

        // 10:05 is the first cron time after the attempt at 10:02.
        clock.Local = Monday.At(10, 5);
        await job.RunAsync(CancellationToken.None);
        Assert.Equal(["a:sync:1", "a:sync:1"], _log.Entries);
    }

    [Fact]
    public async Task Sync_AfterARestart_TheLastSuccessIsTheReference()
    {
        var clock = new FixedTimeProvider(Monday.At(10, 4));
        var tenants = new FakeTenants(_log).Add("a", polling: [new PollingIntegration(1, "*/5 * * * *", Monday.At(10, 1))]);
        var job = SyncJob(tenants, clock);

        await job.RunAsync(CancellationToken.None);
        Assert.Empty(_log.Entries);

        clock.Local = Monday.At(10, 5);
        await job.RunAsync(CancellationToken.None);
        Assert.Equal(["a:sync:1"], _log.Entries);
    }

    [Fact]
    public async Task Sync_AFailedSync_IsNotRetriedBeforeTheNextCronTime()
    {
        var clock = new FixedTimeProvider(Monday.At(10, 0));
        var tenants = new FakeTenants(_log).Add("a", polling: [new PollingIntegration(1, "*/5 * * * *", null)], syncOutcome: new SyncOutcome(SyncOutcomeStatus.Failed, Message: "down"));
        var job = SyncJob(tenants, clock);

        await job.RunAsync(CancellationToken.None);
        clock.Local = Monday.At(10, 1);
        await job.RunAsync(CancellationToken.None);

        Assert.Equal(["a:sync:1"], _log.Entries);
    }

    [Fact]
    public async Task Sync_EachIntegrationIsDueByItsOwnCron()
    {
        var clock = new FixedTimeProvider(Monday.At(10, 30));
        var tenants = new FakeTenants(_log).Add("a", polling:
        [
            new PollingIntegration(1, "*/5 * * * *", Monday.At(10, 26)),
            new PollingIntegration(2, "0 * * * *", Monday.At(10, 0)),
            new PollingIntegration(3, "0 * * * *", Monday.At(10, 0).AddMinutes(-1)),
        ]);

        await SyncJob(tenants, clock).RunAsync(CancellationToken.None);

        // 1: next after 10:26 is 10:30, due. 2: next after 10:00 is 11:00, not due. 3: 10:00 itself is after 09:59, due.
        Assert.Equal(["a:sync:1", "a:sync:3"], _log.Entries);
    }

    [Fact]
    public async Task Sync_AnInvalidCron_IsNeverSynced_AndDoesNotStopTheOthers()
    {
        var clock = new FixedTimeProvider(Monday.At(10, 0));
        var tenants = new FakeTenants(_log).Add("a", polling: [new PollingIntegration(1, "every five minutes", null), new PollingIntegration(2, "*/5 * * * *", null)]);

        await SyncJob(tenants, clock).RunAsync(CancellationToken.None);

        Assert.Equal(["a:sync:2"], _log.Entries);
    }

    [Fact]
    public async Task Sync_ATenantWhoseListingFails_DoesNotStopTheOthers()
    {
        var clock = new FixedTimeProvider(Monday.At(10, 0));
        var tenants = new FakeTenants(_log)
            .Add("a", polling: [new PollingIntegration(1, "*/5 * * * *", null)], throwOnList: true)
            .Add("b", polling: [new PollingIntegration(9, "*/5 * * * *", null)]);

        await SyncJob(tenants, clock).RunAsync(CancellationToken.None);

        Assert.Equal(["b:sync:9"], _log.Entries);
    }

    [Fact]
    public async Task Sync_AnIntegrationWhoseSyncThrows_DoesNotStopTheTenantsOtherIntegrations()
    {
        var clock = new FixedTimeProvider(Monday.At(10, 0));
        var tenants = new FakeTenants(_log).Add("a", polling: [new PollingIntegration(1, "*/5 * * * *", null), new PollingIntegration(2, "*/5 * * * *", null)], throwOnSyncOf: 1);

        await SyncJob(tenants, clock).RunAsync(CancellationToken.None);

        Assert.Equal(["a:sync:1", "a:sync:2"], _log.Entries);
    }

    [Fact]
    public void Sync_FollowsItsOptions()
    {
        var job = new IntegrationSyncJob(new FakeTenants(_log), new FakeTenants(_log), Options.Create(new AttendanceSyncOptions { Enabled = false, TickSeconds = 30 }), new FixedTimeProvider(Monday), NullLogger<IntegrationSyncJob>.Instance);

        Assert.False(job.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(30), job.Schedule.Interval);
    }

    [Fact]
    public async Task Reconciliation_OnlyReconcilesIntegrationsThatHaveSyncedBefore()
    {
        var tenants = new FakeTenants(_log).Add("a", polling:
        [
            new PollingIntegration(1, "*/5 * * * *", Monday.At(9, 0)),
            new PollingIntegration(2, "*/5 * * * *", null),
        ]).Add("b", polling: [new PollingIntegration(5, "0 * * * *", Monday.At(8, 0))]);

        await ReconciliationJob(tenants).RunAsync(CancellationToken.None);

        // Each tenant is reconciled, then its empty sync batches are pruned.
        Assert.Equal(["a:reconcile:1", "a:prune", "b:reconcile:5", "b:prune"], _log.Entries);
    }

    [Fact]
    public async Task Reconciliation_AFailingPrune_DoesNotStopTheOtherTenants()
    {
        var tenants = new FakeTenants(_log)
            .Add("a", polling: [new PollingIntegration(1, "*/5 * * * *", Monday)], throwOnPrune: true)
            .Add("b", polling: [new PollingIntegration(5, "*/5 * * * *", Monday)]);

        await ReconciliationJob(tenants).RunAsync(CancellationToken.None);

        Assert.Equal(["a:reconcile:1", "a:prune", "b:reconcile:5", "b:prune"], _log.Entries);
    }

    [Fact]
    public async Task Reconciliation_AFailingIntegrationOrTenant_DoesNotStopTheOthers()
    {
        var tenants = new FakeTenants(_log)
            .Add("a", polling: [new PollingIntegration(1, "*/5 * * * *", Monday), new PollingIntegration(2, "*/5 * * * *", Monday)], throwOnSyncOf: 1)
            .Add("b", polling: [new PollingIntegration(5, "*/5 * * * *", Monday)], throwOnList: true)
            .Add("c", polling: [new PollingIntegration(8, "*/5 * * * *", Monday)]);

        await ReconciliationJob(tenants).RunAsync(CancellationToken.None);

        Assert.Equal(["a:reconcile:1", "a:reconcile:2", "a:prune", "c:reconcile:8", "c:prune"], _log.Entries);
    }

    [Theory]
    [InlineData(true, true, true, "0 2 * * *")]
    [InlineData(false, true, false, "0 2 * * *")]
    [InlineData(true, false, false, "0 2 * * *")]
    public void Reconciliation_RunsNightlyUnlessEitherSwitchIsOff(bool syncEnabled, bool reconciliationEnabled, bool expectedEnabled, string cron)
    {
        var job = new ReconciliationJob(
            new FakeTenants(_log),
            new FakeTenants(_log),
            Options.Create(new AttendanceSyncOptions { Enabled = syncEnabled, ReconciliationEnabled = reconciliationEnabled, ReconciliationCron = cron }),
            NullLogger<ReconciliationJob>.Instance);

        Assert.Equal(expectedEnabled, job.Enabled);
        Assert.Equal(cron, job.Schedule.Cron);
        Assert.Null(job.Schedule.Interval);
    }

    private NormalizationJob NormalizationJob(FakeTenants tenants) =>
        new(tenants, tenants, Options.Create(new AttendanceNormalizationOptions()), NullLogger<NormalizationJob>.Instance);

    private IntegrationSyncJob SyncJob(FakeTenants tenants, TimeProvider clock) =>
        new(tenants, tenants, Options.Create(new AttendanceSyncOptions()), clock, NullLogger<IntegrationSyncJob>.Instance);

    private ReconciliationJob ReconciliationJob(FakeTenants tenants) =>
        new(tenants, tenants, Options.Create(new AttendanceSyncOptions()), NullLogger<ReconciliationJob>.Instance);

    private sealed class Log
    {
        public List<string> Entries { get; } = [];
    }

    /// <summary>The tenant registry and its scope factory in one: each tenant's scope holds that tenant's fake services.</summary>
    private sealed class FakeTenants : ITenantRegistry, ITenantScopeFactory
    {
        private readonly Log _log;
        private readonly List<TenantDefinition> _tenants = [];
        private readonly Dictionary<string, ServiceProvider> _providers = [];
        private readonly HashSet<string> _broken = [];

        public FakeTenants(Log log)
        {
            _log = log;
        }

        public IReadOnlyList<TenantDefinition> Tenants => _tenants;

        public TenantDefinition? DefaultTenant => null;

        public FakeTenants Add(
            string id,
            int[]? pendingBatches = null,
            PollingIntegration[]? polling = null,
            bool broken = false,
            bool throwOnNormalize = false,
            bool throwOnList = false,
            int? throwOnSyncOf = null,
            SyncOutcome? syncOutcome = null,
            bool throwOnPrune = false)
        {
            _tenants.Add(new TenantDefinition { Id = id, Name = id, ConnectionString = "-" });
            if (broken)
            {
                _broken.Add(id);
            }

            _providers[id] = new ServiceCollection()
                .AddSingleton<INormalizationService>(new FakeNormalization(_log, id, pendingBatches ?? [], throwOnNormalize))
                .AddSingleton<IIntegrationSyncService>(new FakeSync(_log, id, polling ?? [], throwOnList, throwOnSyncOf, syncOutcome ?? new SyncOutcome(SyncOutcomeStatus.Succeeded), throwOnPrune))
                .BuildServiceProvider();
            return this;
        }

        public TenantDefinition? Find(string? tenantId) => _tenants.FirstOrDefault(t => t.Id == tenantId);

        public AsyncServiceScope CreateScope(string tenantId) =>
            _broken.Contains(tenantId) ? throw new InvalidOperationException($"No scope for {tenantId}.") : _providers[tenantId].CreateAsyncScope();
    }

    private sealed class FakeNormalization(Log log, string tenant, int[] pending, bool throwOnNormalize) : INormalizationService
    {
        public Task<IReadOnlyList<int>> ListBatchesWithPendingTransactionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<int>>(pending);

        public Task<NormalizationResult> NormalizeBatchAsync(int batchId, CancellationToken cancellationToken)
        {
            log.Entries.Add($"{tenant}:normalize:{batchId}");
            return throwOnNormalize ? throw new InvalidOperationException("normalization failed") : Task.FromResult(new NormalizationResult(1, 0, 0, 0));
        }
    }

    private sealed class FakeSync(Log log, string tenant, PollingIntegration[] polling, bool throwOnList, int? throwOnSyncOf, SyncOutcome outcome, bool throwOnPrune) : IIntegrationSyncService
    {
        public Task<int> PruneEmptyBatchesAsync(CancellationToken cancellationToken)
        {
            log.Entries.Add($"{tenant}:prune");
            return throwOnPrune ? throw new InvalidOperationException("prune failed") : Task.FromResult(3);
        }

        public Task<IReadOnlyList<PollingIntegration>> ListPollingIntegrationsAsync(CancellationToken cancellationToken) =>
            throwOnList ? throw new InvalidOperationException("listing failed") : Task.FromResult<IReadOnlyList<PollingIntegration>>(polling);

        public Task<SyncOutcome> SyncAsync(int integrationId, CancellationToken cancellationToken) => Run("sync", integrationId);

        public Task<SyncOutcome> ReconcileAsync(int integrationId, CancellationToken cancellationToken) => Run("reconcile", integrationId);

        private Task<SyncOutcome> Run(string what, int integrationId)
        {
            log.Entries.Add($"{tenant}:{what}:{integrationId}");
            return throwOnSyncOf == integrationId ? throw new InvalidOperationException("the sync threw") : Task.FromResult(outcome);
        }
    }
}
