using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>Integration setup (screens phase 2): integrations, devices, vendor-id mappings and run-now.</summary>
public sealed class AttendanceIntegrationServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly FakeSync _sync = new();
    private readonly int _csvProviderId;
    private readonly int _inactiveProviderId;
    private readonly int _employeeId;

    public AttendanceIntegrationServiceTests()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var csv = new AttendanceProvider { ProviderKey = "generic-csv", DisplayName = "Generic CSV", IsActive = true };
        var zk = new AttendanceProvider { ProviderKey = "zkteco", DisplayName = "ZKTeco", IsActive = false };
        var ada = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        db.AddRange(csv, zk, ada);
        db.SaveChanges();
        (_csvProviderId, _inactiveProviderId, _employeeId) = (csv.Id, zk.Id, ada.Id);
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AnIntegration_IsCreated_Trimmed_AndAudited()
    {
        var result = await CreateAsync(Input(name: "  HQ clock  ", reference: " hq-clock ", cron: " */5 * * * * "));

        Assert.True(result.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        Assert.Equal(("HQ clock", "hq-clock", "*/5 * * * *", AttendanceIntegrationStatus.Active), (integration.DisplayName, integration.ConnectionConfigRef, integration.PollingScheduleCron, integration.Status));
        Assert.Equal(_csvProviderId, integration.ProviderId);
        var entry = Assert.Single(db.AttendanceAuditLogs);
        Assert.Equal((AttendanceAuditActions.IntegrationChanged, integration.Id), (entry.Action, entry.TargetId));
    }

    [Fact]
    public async Task ABlankCronAndReference_AreAllowed_ForAnIntegrationYouUploadFilesTo()
    {
        var result = await CreateAsync(Input(reference: " ", cron: " "));

        Assert.True(result.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal((string.Empty, null), (db.AttendanceIntegrations.Single().ConnectionConfigRef, db.AttendanceIntegrations.Single().PollingScheduleCron));
    }

    [Theory]
    [InlineData("ProviderId", 999, "does not exist")]
    public async Task AnUnknownProvider_IsRefused(string field, int providerId, string fragment)
    {
        var result = await CreateAsync(Input(providerId: providerId));

        Assert.Contains(fragment, result.Errors[field]);
    }

    [Fact]
    public async Task AProviderWithNoAdapter_CanNotBeChosen()
    {
        var result = await CreateAsync(Input(providerId: _inactiveProviderId));

        Assert.Contains("no adapter", result.Errors[nameof(IntegrationInput.ProviderId)]);
    }

    [Theory]
    [InlineData("every five minutes")]
    [InlineData("* * * *")]
    [InlineData("99 * * * *")]
    public async Task ABadCron_IsRefused(string cron)
    {
        Assert.Contains("cron", (await CreateAsync(Input(cron: cron))).Errors[nameof(IntegrationInput.PollingScheduleCron)]);
    }

    [Theory]
    [InlineData("Host=db;Password=secret")]
    [InlineData("has a space")]
    [InlineData("-leading-dash")]
    [InlineData("semi;colon")]
    public async Task ASecretShapedReference_IsRefused_OnlyANameIsAccepted(string reference)
    {
        var result = await CreateAsync(Input(reference: reference));

        Assert.Contains("never the secret itself", result.Errors[nameof(IntegrationInput.ConnectionConfigRef)]);
    }

    [Theory]
    [InlineData("acme-clock-api")]
    [InlineData("kv:acme/clock")]
    [InlineData("A1.b_2")]
    public async Task ANameShapedReference_IsAccepted(string reference)
    {
        Assert.True((await CreateAsync(Input(reference: reference))).Succeeded);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("nonsense")]
    [InlineData("")]
    public async Task OnlyActiveOrPaused_CanBeChosen(string status)
    {
        Assert.Contains("Active or Paused", (await CreateAsync(Input(status: status))).Errors[nameof(IntegrationInput.Status)]);
    }

    [Fact]
    public async Task ABlankName_IsRefused()
    {
        Assert.Contains("name is required", (await CreateAsync(Input(name: "  "))).Errors[nameof(IntegrationInput.DisplayName)]);
    }

    [Fact]
    public async Task AnIntegration_CanBePaused_AndAFailedOneBroughtBack()
    {
        var id = (await CreateAsync(Input())).Id!.Value;

        Assert.True((await UpdateAsync(id, Input(status: "Paused"))).Succeeded);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(AttendanceIntegrationStatus.Paused, db.AttendanceIntegrations.Single().Status);
            db.AttendanceIntegrations.Single().Status = AttendanceIntegrationStatus.Failed;
            db.SaveChanges();
        }

        Assert.True((await UpdateAsync(id, Input(status: "Active"))).Succeeded);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceIntegrationStatus.Active, check.AttendanceIntegrations.Single().Status);
        Assert.Equal(2, check.AttendanceAuditLogs.Count(a => a.Details!.Contains("Changed.")));
    }

    [Fact]
    public async Task TheProvider_CanNotBeChangedAfterwards()
    {
        var id = (await CreateAsync(Input())).Id!.Value;

        var result = await UpdateAsync(id, Input(providerId: _inactiveProviderId));

        Assert.Contains("can't be changed", result.Errors[nameof(IntegrationInput.ProviderId)]);
    }

    [Fact]
    public async Task UpdatingAnUnknownIntegration_IsNotFound()
    {
        Assert.True((await UpdateAsync(999, Input())).NotFound);
    }

    [Fact]
    public async Task TheList_ShowsCounts_Provider_AndHealth_AndTheInputRoundTrips()
    {
        var id = (await CreateAsync(Input(name: "B clock", reference: "b-ref", cron: "0 * * * *"))).Id!.Value;
        await CreateAsync(Input(name: "A clock"));
        await AddDeviceAsync(id, Device("DEV-1"));
        await AddDeviceAsync(id, Device("DEV-2"));
        await AddMappingAsync(id, new MappingInput { ExternalEmployeeId = "X-1", EmployeeId = _employeeId });

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = CreateService(db);
        var list = await service.ListAsync(CancellationToken.None);

        Assert.Equal(["A clock", "B clock"], list.Select(i => i.DisplayName));
        var b = list.Single(i => i.DisplayName == "B clock");
        Assert.Equal((2, 1, "generic-csv", "Generic CSV", "Unknown", "Active"), (b.DeviceCount, b.MappingCount, b.ProviderKey, b.ProviderName, b.HealthStatus, b.Status));
        Assert.Equal(("b-ref", "0 * * * *"), (b.ConnectionConfigRef, b.PollingScheduleCron));
        var input = (await service.GetInputAsync(id, CancellationToken.None))!;
        Assert.Equal(("B clock", "b-ref", "0 * * * *", "Active", _csvProviderId), (input.DisplayName, input.ConnectionConfigRef, input.PollingScheduleCron, input.Status, input.ProviderId));
        Assert.Null(await service.GetAsync(999, CancellationToken.None));
        Assert.Null(await service.GetInputAsync(999, CancellationToken.None));
    }

    [Fact]
    public async Task TheProviders_ListActiveOnesFirst()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var providers = await CreateService(db).ListProvidersAsync(CancellationToken.None);

        Assert.Equal([("generic-csv", true), ("zkteco", false)], providers.Select(p => (p.Key, p.IsActive)));
    }

    [Fact]
    public async Task TheEmployeeOptions_AreTheActiveEmployees_ByName()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.Profiles.Add(new Profile { EmployeeNumber = "E-0", FirstName = "Old", LastName = "Timer", Title = "Old Timer", IsActive = false });
            db.Profiles.Add(new Profile { EmployeeNumber = "E-2", FirstName = "Alan", LastName = "Turing", Title = "Alan Turing" });
            db.SaveChanges();
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);

        Assert.Equal(["Ada Lovelace (E-1)", "Alan Turing (E-2)"], (await CreateService(check).ListEmployeeOptionsAsync(CancellationToken.None)).Select(o => o.Name));
    }

    [Fact]
    public async Task ADevice_IsAdded_Updated_AndDeleted()
    {
        var integrationId = (await CreateAsync(Input())).Id!.Value;

        var added = await AddDeviceAsync(integrationId, Device(" DEV-1 ", name: " Front door ", zone: "Asia/Beirut", type: "facial"));
        Assert.True(added.Succeeded);
        var deviceId = added.Id!.Value;
        Assert.True((await UpdateDeviceAsync(integrationId, deviceId, Device("DEV-1", name: "Main entrance", zone: "UTC", type: "Facial"))).Succeeded);

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var device = db.AttendanceDevices.Single();
            Assert.Equal(("DEV-1", "Main entrance", AttendanceDeviceType.Facial, "UTC"), (device.ExternalDeviceId, device.Name, device.DeviceType, device.TimeZoneId));
        }

        Assert.True((await DeleteDeviceAsync(integrationId, deviceId)).Succeeded);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.AttendanceDevices);
        Assert.Equal(3, check.AttendanceAuditLogs.Count(a => a.TargetType == nameof(AttendanceDevice)));
    }

    [Theory]
    [InlineData("", "Front", "UTC", "Biometric", "ExternalDeviceId", "required")]
    [InlineData("DEV-1", "", "UTC", "Biometric", "Name", "required")]
    [InlineData("DEV-1", "Front", "Mars/Olympus", "Biometric", "TimeZoneId", "IANA")]
    [InlineData("DEV-1", "Front", "", "Biometric", "TimeZoneId", "IANA")]
    [InlineData("DEV-1", "Front", "UTC", "Toaster", "DeviceType", "must be one of")]
    public async Task ABadDevice_IsRefused(string external, string name, string zone, string type, string field, string fragment)
    {
        var integrationId = (await CreateAsync(Input())).Id!.Value;

        var result = await AddDeviceAsync(integrationId, new DeviceInput { ExternalDeviceId = external, Name = name, TimeZoneId = zone, DeviceType = type });

        Assert.Contains(fragment, result.Errors[field]);
    }

    [Fact]
    public async Task ADeviceId_IsUniqueWithinAnIntegration_NotAcrossThem()
    {
        var first = (await CreateAsync(Input(name: "One"))).Id!.Value;
        var second = (await CreateAsync(Input(name: "Two"))).Id!.Value;
        Assert.True((await AddDeviceAsync(first, Device("DEV-1"))).Succeeded);

        Assert.Contains("already has this id", (await AddDeviceAsync(first, Device("DEV-1"))).Errors[nameof(DeviceInput.ExternalDeviceId)]);
        Assert.True((await AddDeviceAsync(second, Device("DEV-1"))).Succeeded);

        // Renaming a device to its own id is not a clash with itself.
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var ownId = db.AttendanceDevices.First(d => d.IntegrationId == first).Id;
        Assert.True((await UpdateDeviceAsync(first, ownId, Device("DEV-1", name: "Renamed"))).Succeeded);
    }

    [Fact]
    public async Task ADeviceWithPunches_IsKept_AndTheReasonIsGiven()
    {
        var integrationId = (await CreateAsync(Input())).Id!.Value;
        var deviceId = (await AddDeviceAsync(integrationId, Device("DEV-1"))).Id!.Value;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var batch = new AttendanceProcessingBatch { IntegrationId = integrationId, BatchType = AttendanceBatchType.ManualUpload };
            db.RawAttendanceTransactions.Add(new RawAttendanceTransaction
            {
                IntegrationId = integrationId,
                ImportBatch = batch,
                DeviceId = deviceId,
                TransactionTimestampRaw = "-",
                RawPayload = "{}",
                SourceSystem = "generic-csv",
                RecordHash = "punch",
            });
            db.SaveChanges();
        }

        var result = await DeleteDeviceAsync(integrationId, deviceId);

        Assert.Contains("punches on record", result.Errors["id"]);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Single(check.AttendanceDevices);
        Assert.Single(check.RawAttendanceTransactions);
    }

    [Fact]
    public async Task ADeviceOfAnotherIntegration_IsNotFound()
    {
        var first = (await CreateAsync(Input(name: "One"))).Id!.Value;
        var second = (await CreateAsync(Input(name: "Two"))).Id!.Value;
        var deviceId = (await AddDeviceAsync(first, Device("DEV-1"))).Id!.Value;

        Assert.True((await UpdateDeviceAsync(second, deviceId, Device("DEV-1"))).NotFound);
        Assert.True((await DeleteDeviceAsync(second, deviceId)).NotFound);
        Assert.True((await AddDeviceAsync(999, Device("DEV-1"))).NotFound);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Null(await CreateService(db).ListDevicesAsync(999, CancellationToken.None));
    }

    [Fact]
    public async Task AMapping_IsAdded_ListedWithTheEmployeesName_AndDeleted()
    {
        var integrationId = (await CreateAsync(Input())).Id!.Value;

        var added = await AddMappingAsync(integrationId, new MappingInput { ExternalEmployeeId = " EXT-1 ", EmployeeId = _employeeId });

        Assert.True(added.Succeeded);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var mapping = Assert.Single((await CreateService(db).ListMappingsAsync(integrationId, CancellationToken.None))!);
            Assert.Equal(("EXT-1", _employeeId, "Ada Lovelace"), (mapping.ExternalEmployeeId, mapping.EmployeeId, mapping.EmployeeName));
        }

        Assert.True((await DeleteMappingAsync(integrationId, added.Id!.Value)).Succeeded);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.ExternalEmployeeIdMappings);
    }

    [Fact]
    public async Task AMapping_NeedsAKnownEmployee_AndAnUnusedVendorId_PerIntegration_CaseSensitively()
    {
        var first = (await CreateAsync(Input(name: "One"))).Id!.Value;
        var second = (await CreateAsync(Input(name: "Two"))).Id!.Value;
        Assert.True((await AddMappingAsync(first, new MappingInput { ExternalEmployeeId = "E-1", EmployeeId = _employeeId })).Succeeded);

        Assert.Contains("already mapped", (await AddMappingAsync(first, new MappingInput { ExternalEmployeeId = "E-1", EmployeeId = _employeeId })).Errors[nameof(MappingInput.ExternalEmployeeId)]);
        Assert.True((await AddMappingAsync(first, new MappingInput { ExternalEmployeeId = "e-1", EmployeeId = _employeeId })).Succeeded);
        Assert.True((await AddMappingAsync(second, new MappingInput { ExternalEmployeeId = "E-1", EmployeeId = _employeeId })).Succeeded);
        Assert.Contains("does not exist", (await AddMappingAsync(second, new MappingInput { ExternalEmployeeId = "Z", EmployeeId = 999 })).Errors[nameof(MappingInput.EmployeeId)]);
        Assert.Contains("required", (await AddMappingAsync(second, new MappingInput { ExternalEmployeeId = "  ", EmployeeId = _employeeId })).Errors[nameof(MappingInput.ExternalEmployeeId)]);
        Assert.True((await AddMappingAsync(999, new MappingInput { ExternalEmployeeId = "Q", EmployeeId = _employeeId })).NotFound);
        Assert.True((await DeleteMappingAsync(second, 99999)).NotFound);
    }

    [Fact]
    public async Task RunNow_AsksTheSyncService_IsAudited_AndReportsTheOutcome()
    {
        var id = (await CreateAsync(Input())).Id!.Value;
        _sync.Outcome = new SyncOutcome(SyncOutcomeStatus.Succeeded, 12, 3);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = CreateService(db);
        var sync = await service.RunAsync(id, reconcile: false, CancellationToken.None);
        _sync.Outcome = new SyncOutcome(SyncOutcomeStatus.Failed, Message: "down");
        var reconcile = await service.RunAsync(id, reconcile: true, CancellationToken.None);

        Assert.Equal([("sync", id), ("reconcile", id)], _sync.Runs);
        Assert.Equal(("Succeeded", 12, 3), (sync!.Status, sync.BatchId, sync.RecordsIngested));
        Assert.Equal(("Failed", "down"), (reconcile!.Status, reconcile.Message));
        Assert.Equal(["Reconciliation requested.", "Sync requested."], db.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.IntegrationRunRequested).OrderByDescending(a => a.Id).Select(a => a.Details));
        Assert.Null(await service.RunAsync(999, false, CancellationToken.None));
        Assert.Equal(2, _sync.Runs.Count);
    }

    [Fact]
    public async Task AnotherTenant_SeesNone_OfThisTenantsIntegrations()
    {
        await CreateAsync(Input());

        using var other = _database.CreateContext(TestDatabase.TenantB);
        Assert.Empty(await CreateService(other).ListAsync(CancellationToken.None));
    }

    private static IntegrationInput Input(int providerId = 0, string name = "HQ clock", string? reference = null, string? cron = null, string status = "Active") => new()
    {
        ProviderId = providerId,
        DisplayName = name,
        ConnectionConfigRef = reference,
        PollingScheduleCron = cron,
        Status = status,
    };

    private static DeviceInput Device(string external, string name = "Front door", string zone = "UTC", string type = "Biometric") => new()
    {
        ExternalDeviceId = external,
        Name = name,
        TimeZoneId = zone,
        DeviceType = type,
    };

    private async Task<robeem.Application.Common.SaveResult> CreateAsync(IntegrationInput input)
    {
        input.ProviderId = input.ProviderId == 0 ? _csvProviderId : input.ProviderId;
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).CreateAsync(input, CancellationToken.None);
    }

    private async Task<robeem.Application.Common.SaveResult> UpdateAsync(int id, IntegrationInput input)
    {
        input.ProviderId = input.ProviderId == 0 ? _csvProviderId : input.ProviderId;
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).UpdateAsync(id, input, CancellationToken.None);
    }

    private async Task<robeem.Application.Common.SaveResult> AddDeviceAsync(int integrationId, DeviceInput input)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).AddDeviceAsync(integrationId, input, CancellationToken.None);
    }

    private async Task<robeem.Application.Common.SaveResult> UpdateDeviceAsync(int integrationId, int deviceId, DeviceInput input)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).UpdateDeviceAsync(integrationId, deviceId, input, CancellationToken.None);
    }

    private async Task<robeem.Application.Common.SaveResult> DeleteDeviceAsync(int integrationId, int deviceId)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).DeleteDeviceAsync(integrationId, deviceId, CancellationToken.None);
    }

    private async Task<robeem.Application.Common.SaveResult> AddMappingAsync(int integrationId, MappingInput input)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).AddMappingAsync(integrationId, input, CancellationToken.None);
    }

    private async Task<robeem.Application.Common.SaveResult> DeleteMappingAsync(int integrationId, int mappingId)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).DeleteMappingAsync(integrationId, mappingId, CancellationToken.None);
    }

    private AttendanceIntegrationService CreateService(RobeemDbContext db) =>
        new(db, _sync, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System));

    private sealed class FakeSync : IIntegrationSyncService
    {
        public SyncOutcome Outcome { get; set; } = new(SyncOutcomeStatus.Succeeded);

        public List<(string Kind, int IntegrationId)> Runs { get; } = [];

        public Task<IReadOnlyList<PollingIntegration>> ListPollingIntegrationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> SyncAsync(int integrationId, CancellationToken cancellationToken)
        {
            Runs.Add(("sync", integrationId));
            return Task.FromResult(Outcome);
        }

        public Task<SyncOutcome> ReconcileAsync(int integrationId, CancellationToken cancellationToken)
        {
            Runs.Add(("reconcile", integrationId));
            return Task.FromResult(Outcome);
        }

        public Task<int> PruneEmptyBatchesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
