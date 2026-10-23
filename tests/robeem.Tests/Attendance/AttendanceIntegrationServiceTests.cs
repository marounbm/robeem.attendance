using Microsoft.Extensions.DependencyInjection;
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
    private readonly int _unreadableProviderId;
    private readonly int _employeeId;
    private readonly Dictionary<string, int> _deviceTypeIds;

    public AttendanceIntegrationServiceTests()
    {
        _database.SeedAttendanceCatalog();
        using var db = _database.CreateContext(TestDatabase.TenantA);

        // The seeded Generic CSV provider (default file format: usable), given two more transports; the seeded ZKTeco one switched off;
        // and one that is active but has neither a default file format nor a code adapter.
        var csv = db.AttendanceProviders.Single(p => p.ProviderKey == "generic-csv");
        foreach (var key in new[] { "Webhook", "RestApi" })
        {
            csv.Transports.Add(new AttendanceProviderTransport { Transport = db.AttendanceTransportDefinitions.Single(t => t.TransportKey == key) });
        }

        var zk = db.AttendanceProviders.Single(p => p.ProviderKey == "zkteco");
        zk.IsActive = false;
        var unreadable = new AttendanceProvider { ProviderKey = "unreadable", DisplayName = "Unreadable" };
        var ada = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        db.AddRange(unreadable, ada);
        db.SaveChanges();
        (_csvProviderId, _inactiveProviderId, _unreadableProviderId, _employeeId) = (csv.Id, zk.Id, unreadable.Id, ada.Id);
        _deviceTypeIds = db.AttendanceDeviceTypes.ToDictionary(t => t.Name, t => t.Id, StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AnIntegration_IsCreated_Trimmed_AndAudited()
    {
        var result = await CreateAsync(Input(name: "  HQ clock  ", reference: " hq-clock ", cron: " */5 * * * * ", transport: "RestApi"));

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

    [Fact]
    public async Task TheTransport_MustBeOneTheProviderSupports()
    {
        var refused = await CreateAsync(Input(transport: "Sftp"));
        var unknown = await CreateAsync(Input(transport: "Carrier pigeon"));
        var accepted = await CreateAsync(Input(transport: "Webhook"));

        Assert.Contains("can't receive attendance by Sftp", refused.Errors[nameof(IntegrationInput.Transport)]);
        Assert.Contains("Choose how", unknown.Errors[nameof(IntegrationInput.Transport)]);
        Assert.True(accepted.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(AttendanceTransport.Webhook, db.AttendanceIntegrations.Single().Transport);
    }

    [Fact]
    public async Task ThePollingSchedule_IsKeptOnlyForAnIntegrationThatPulls()
    {
        var upload = await CreateAsync(Input(name: "Upload", cron: "0 * * * *", transport: "FileUpload"));
        var pull = await CreateAsync(Input(name: "Pull", cron: "0 * * * *", transport: "RestApi"));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Null(db.AttendanceIntegrations.Single(i => i.Id == upload.Id).PollingScheduleCron);
        Assert.Equal("0 * * * *", db.AttendanceIntegrations.Single(i => i.Id == pull.Id).PollingScheduleCron);
    }

    [Fact]
    public async Task TheTransport_CanBeChangedWithinWhatTheProviderSupports()
    {
        var id = (await CreateAsync(Input())).Id!.Value;

        var refused = await UpdateAsync(id, Input(transport: "Database"));
        var changed = await UpdateAsync(id, Input(transport: "Webhook"));

        Assert.Contains("can't receive attendance by Database", refused.Errors[nameof(IntegrationInput.Transport)]);
        Assert.True(changed.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal("Webhook", (await CreateService(db).GetInputAsync(id, CancellationToken.None))!.Transport);
    }

    [Theory]
    [InlineData("ProviderId", 999, "does not exist")]
    public async Task AnUnknownProvider_IsRefused(string field, int providerId, string fragment)
    {
        var result = await CreateAsync(Input(providerId: providerId));

        Assert.Contains(fragment, result.Errors[field]);
    }

    [Fact]
    public async Task AnInactiveProvider_CanNotBeChosen()
    {
        var result = await CreateAsync(Input(providerId: _inactiveProviderId));

        Assert.Contains("not active", result.Errors[nameof(IntegrationInput.ProviderId)]);
    }

    [Fact]
    public async Task AProviderNothingCanRead_CanNotBeChosen_UntilItHasAFormatOrAnAdapter()
    {
        var refused = await CreateAsync(Input(providerId: _unreadableProviderId));
        Assert.Contains("neither a default file format nor a code adapter", refused.Errors[nameof(IntegrationInput.ProviderId)]);

        // A code adapter registered under its key is enough.
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var withAdapter = new AttendanceIntegrationService(db, _sync, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), new ServiceCollection().BuildServiceProvider(), new StubAdapterCatalog("unreadable"));
        Assert.True((await withAdapter.CreateAsync(Input(providerId: _unreadableProviderId), CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task TheProviderList_SaysWhichProvidersAreUsable_AndWhichTransportsEachOffers()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var providers = await CreateService(db).ListProvidersAsync(CancellationToken.None);

        var csv = providers.Single(p => p.Key == "generic-csv");
        Assert.True(csv.IsActive);
        Assert.Equal(["FileUpload", "RestApi", "Webhook"], csv.SupportedTransports.Select(t => t.Key).Order(StringComparer.Ordinal));
        Assert.Equal("FileUpload", csv.SupportedTransports[^1].Key);
        Assert.Contains(csv.SupportedTransports, t => t.Key == "RestApi" && t.DisplayName == "REST API" && t.IsPull && !t.UsesFileFormat);

        // ZKTeco is switched off; "unreadable" and BioStar are active but have no default format, and this service's catalog has no adapter for them.
        Assert.False(providers.Single(p => p.Key == "zkteco").IsActive);
        Assert.False(providers.Single(p => p.Key == "unreadable").IsActive);
        Assert.False(providers.Single(p => p.Key == "suprema-biostar").IsActive);
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
        var id = (await CreateAsync(Input(name: "B clock", reference: "b-ref", cron: "0 * * * *", transport: "RestApi"))).Id!.Value;
        await CreateAsync(Input(name: "A clock"));
        var firstDevice = (await AddDeviceAsync(id, Device("DEV-1"))).Id!.Value;
        await AddDeviceAsync(id, Device("DEV-2"));
        await AddMappingAsync(id, firstDevice, new MappingInput { ExternalEmployeeId = "X-1", EmployeeId = _employeeId });

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = CreateService(db);
        var list = await service.ListAsync(CancellationToken.None);

        Assert.Equal(["A clock", "B clock"], list.Select(i => i.DisplayName));
        var b = list.Single(i => i.DisplayName == "B clock");
        Assert.Equal((2, 1, "generic-csv", "Generic CSV upload", "Unknown", "Active"), (b.DeviceCount, b.MappingCount, b.ProviderKey, b.ProviderName, b.HealthStatus, b.Status));
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

        // Usable ones first (here only generic CSV has a default format), then the rest by name.
        Assert.Equal(
            [("generic-csv", true), ("generic-webhook", false), ("suprema-biostar", false), ("unreadable", false), ("zkteco", false)],
            providers.Select(p => (p.Key, p.IsActive)));
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
            Assert.Equal(("DEV-1", "Main entrance", _deviceTypeIds["Facial"], "UTC"), (device.ExternalDeviceId, device.Name, device.DeviceTypeId, device.TimeZoneId));
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
    [InlineData("DEV-1", "Front", "UTC", "Toaster", "DeviceTypeId", "active device types")]
    public async Task ABadDevice_IsRefused(string external, string name, string zone, string type, string field, string fragment)
    {
        var integrationId = (await CreateAsync(Input())).Id!.Value;

        var result = await AddDeviceAsync(integrationId, new DeviceInput { ExternalDeviceId = external, Name = name, TimeZoneId = zone, DeviceTypeId = _deviceTypeIds.GetValueOrDefault(type, 999) });

        Assert.Contains(fragment, result.Errors[field]);
    }

    [Fact]
    public async Task ADevicesModelAndVersions_AreKeptTrimmed_Listed_AndLengthChecked()
    {
        var integrationId = (await CreateAsync(Input())).Id!.Value;
        var u350 = Device("SN-U350-1", name: "Front door");
        u350.Model = " U350 ";
        u350.SoftwareVersion = " Fingerprint Attendance System 4.8.7 ";
        u350.SdkVersion = " 6.2.5.5 ";

        Assert.True((await AddDeviceAsync(integrationId, u350)).Succeeded);
        Assert.True((await AddDeviceAsync(integrationId, Device("DEV-2", name: "Back door"))).Succeeded);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var listed = (await CreateService(db).ListDevicesAsync(integrationId, CancellationToken.None))!;
        var front = listed.Single(d => d.ExternalDeviceId == "SN-U350-1");
        Assert.Equal(("U350", "Fingerprint Attendance System 4.8.7", "6.2.5.5"), (front.Model, front.SoftwareVersion, front.SdkVersion));
        Assert.Equal(string.Empty, listed.Single(d => d.ExternalDeviceId == "DEV-2").Model);

        var tooLong = Device("DEV-3");
        tooLong.Model = new string('m', 101);
        tooLong.SoftwareVersion = new string('s', 101);
        tooLong.SdkVersion = new string('k', 51);
        var refused = await AddDeviceAsync(integrationId, tooLong);
        Assert.Equal([nameof(DeviceInput.Model), nameof(DeviceInput.SdkVersion), nameof(DeviceInput.SoftwareVersion)], refused.Errors.Keys.Order());
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
        var deviceId = (await AddDeviceAsync(integrationId, Device("DEV-1"))).Id!.Value;

        var added = await AddMappingAsync(integrationId, deviceId, new MappingInput { ExternalEmployeeId = " EXT-1 ", EmployeeId = _employeeId });

        Assert.True(added.Succeeded);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var mapping = Assert.Single((await CreateService(db).ListMappingsAsync(integrationId, deviceId, CancellationToken.None))!);
            Assert.Equal(("EXT-1", _employeeId, "Ada Lovelace"), (mapping.ExternalEmployeeId, mapping.EmployeeId, mapping.EmployeeName));
        }

        Assert.True((await DeleteMappingAsync(integrationId, deviceId, added.Id!.Value)).Succeeded);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.ExternalEmployeeIdMappings);
    }

    [Fact]
    public async Task AMapping_NeedsAKnownEmployee_AndAnUnusedVendorId_PerDevice_CaseSensitively()
    {
        var integrationId = (await CreateAsync(Input(name: "One"))).Id!.Value;
        var first = (await AddDeviceAsync(integrationId, Device("DEV-1"))).Id!.Value;
        var second = (await AddDeviceAsync(integrationId, Device("DEV-2"))).Id!.Value;
        Assert.True((await AddMappingAsync(integrationId, first, new MappingInput { ExternalEmployeeId = "E-1", EmployeeId = _employeeId })).Succeeded);

        Assert.Contains("already mapped on this device", (await AddMappingAsync(integrationId, first, new MappingInput { ExternalEmployeeId = "E-1", EmployeeId = _employeeId })).Errors[nameof(MappingInput.ExternalEmployeeId)]);
        Assert.True((await AddMappingAsync(integrationId, first, new MappingInput { ExternalEmployeeId = "e-1", EmployeeId = _employeeId })).Succeeded);

        // The same vendor id on another device of the same integration is a different mapping.
        Assert.True((await AddMappingAsync(integrationId, second, new MappingInput { ExternalEmployeeId = "E-1", EmployeeId = _employeeId })).Succeeded);
        Assert.Contains("does not exist", (await AddMappingAsync(integrationId, second, new MappingInput { ExternalEmployeeId = "Z", EmployeeId = 999 })).Errors[nameof(MappingInput.EmployeeId)]);
        Assert.Contains("required", (await AddMappingAsync(integrationId, second, new MappingInput { ExternalEmployeeId = "  ", EmployeeId = _employeeId })).Errors[nameof(MappingInput.ExternalEmployeeId)]);
        Assert.True((await AddMappingAsync(integrationId, 99999, new MappingInput { ExternalEmployeeId = "Q", EmployeeId = _employeeId })).NotFound);
        Assert.True((await DeleteMappingAsync(integrationId, second, 99999)).NotFound);
    }

    [Fact]
    public async Task ADeviceOfAnotherIntegration_HasNoMappingsToReadOrChange()
    {
        var first = (await CreateAsync(Input(name: "One"))).Id!.Value;
        var second = (await CreateAsync(Input(name: "Two"))).Id!.Value;
        var deviceId = (await AddDeviceAsync(first, Device("DEV-1"))).Id!.Value;
        var added = await AddMappingAsync(first, deviceId, new MappingInput { ExternalEmployeeId = "E-1", EmployeeId = _employeeId });

        Assert.True((await AddMappingAsync(second, deviceId, new MappingInput { ExternalEmployeeId = "E-2", EmployeeId = _employeeId })).NotFound);
        Assert.True((await DeleteMappingAsync(second, deviceId, added.Id!.Value)).NotFound);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Null(await CreateService(db).ListMappingsAsync(second, deviceId, CancellationToken.None));
        Assert.Single(db.ExternalEmployeeIdMappings);
    }

    [Fact]
    public async Task ADeviceWithMappings_IsKeptUntilTheyAreRemoved_AndTheGridCountsThem()
    {
        var integrationId = (await CreateAsync(Input())).Id!.Value;
        var deviceId = (await AddDeviceAsync(integrationId, Device("DEV-1"))).Id!.Value;
        var mappingId = (await AddMappingAsync(integrationId, deviceId, new MappingInput { ExternalEmployeeId = "E-1", EmployeeId = _employeeId })).Id!.Value;

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var listed = Assert.Single(await CreateService(db).ListAllDevicesAsync(CancellationToken.None));
            Assert.Equal((deviceId, integrationId, 1), (listed.Id, listed.IntegrationId, listed.MappingCount));
            Assert.Equal(listed.Id, (await CreateService(db).GetDeviceAsync(integrationId, deviceId, CancellationToken.None))!.Id);
            Assert.Null(await CreateService(db).GetDeviceAsync(integrationId, 99999, CancellationToken.None));
        }

        var refused = await DeleteDeviceAsync(integrationId, deviceId);
        Assert.Contains("1 employee-id mapping(s)", refused.Errors["id"]);

        Assert.True((await DeleteMappingAsync(integrationId, deviceId, mappingId)).Succeeded);
        Assert.True((await DeleteDeviceAsync(integrationId, deviceId)).Succeeded);
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

    [Fact]
    public async Task TheIntegrationsTimeZone_IsKeptTrimmed_ClearedWhenBlank_AndAuditedOnChange()
    {
        var id = (await CreateAsync(Input(zone: " Asia/Beirut "))).Id!.Value;

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal("Asia/Beirut", db.AttendanceIntegrations.Single().TimeZoneId);
            Assert.Equal("Asia/Beirut", (await CreateService(db).GetInputAsync(id, CancellationToken.None))!.TimeZoneId);
            Assert.Equal("Asia/Beirut", (await CreateService(db).GetAsync(id, CancellationToken.None))!.TimeZoneId);
        }

        Assert.True((await UpdateAsync(id, Input(zone: "Europe/London"))).Succeeded);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var change = db.AttendanceAuditLogs.OrderByDescending(a => a.Id).First().Details!;
            Assert.Contains("zone 'Asia/Beirut'", change);
            Assert.Contains("zone 'Europe/London'", change);
        }

        Assert.True((await UpdateAsync(id, Input(zone: "  "))).Succeeded);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Null(db.AttendanceIntegrations.Single().TimeZoneId);
        }
    }

    [Theory]
    [InlineData("Not/AZone")]
    [InlineData("Mars")]
    public async Task AnUnknownTimeZone_IsRefused(string zone)
    {
        var result = await CreateAsync(Input(zone: zone));

        Assert.False(result.Succeeded);
        Assert.Contains(nameof(IntegrationInput.TimeZoneId), result.Errors.Keys);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceIntegrations);
    }

    [Fact]
    public async Task AZoneLongerThan64Characters_IsRefused()
    {
        var result = await CreateAsync(Input(zone: new string('a', 65)));

        Assert.Contains(nameof(IntegrationInput.TimeZoneId), result.Errors.Keys);
    }

    [Fact]
    public async Task TheDeviceForUploads_MustBeAnOwnDeviceOfTheIntegration_RoundTrips_AndCanNotBeDeletedWhileChosen()
    {
        var id = (await CreateAsync(Input())).Id!.Value;
        var otherId = (await CreateAsync(Input(name: "Other clock"))).Id!.Value;
        var device = (await AddDeviceAsync(id, Device("U350", name: "U350"))).Id!.Value;
        var foreign = (await AddDeviceAsync(otherId, Device("X1"))).Id!.Value;

        Assert.Contains(nameof(IntegrationInput.DefaultDeviceId), (await UpdateAsync(id, Input(defaultDeviceId: foreign))).Errors.Keys);
        Assert.Contains(nameof(IntegrationInput.DefaultDeviceId), (await UpdateAsync(id, Input(defaultDeviceId: 99999))).Errors.Keys);
        Assert.True((await UpdateAsync(id, Input(defaultDeviceId: device))).Succeeded);

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var service = CreateService(db);
            Assert.Equal(device, (await service.GetInputAsync(id, CancellationToken.None))!.DefaultDeviceId);
            var item = (await service.GetAsync(id, CancellationToken.None))!;
            Assert.Equal((device, "U350"), (item.DefaultDeviceId, item.DefaultDeviceName));
            Assert.Contains($"device {device}", db.AttendanceAuditLogs.OrderByDescending(a => a.Id).First().Details);
        }

        var refused = await DeleteDeviceAsync(id, device);
        Assert.False(refused.Succeeded);
        Assert.Contains("device for uploaded files", refused.Errors["id"]);

        Assert.True((await UpdateAsync(id, Input())).Succeeded);
        Assert.True((await DeleteDeviceAsync(id, device)).Succeeded);
    }

    [Fact]
    public async Task TheDeviceForUploads_CanNotBeChosenWhileCreating_AndIsDroppedForAnIntegrationThatDoesNotReceiveFiles()
    {
        var refused = await CreateAsync(Input(defaultDeviceId: 5));
        Assert.Contains(nameof(IntegrationInput.DefaultDeviceId), refused.Errors.Keys);

        // For another transport it is not an error, only not kept (like a file format).
        Assert.True((await CreateAsync(Input(transport: "RestApi", defaultDeviceId: 5))).Succeeded);
        var id = (await CreateAsync(Input(name: "Second"))).Id!.Value;
        var device = (await AddDeviceAsync(id, Device("U350"))).Id!.Value;
        Assert.True((await UpdateAsync(id, Input(name: "Second", defaultDeviceId: device))).Succeeded);

        Assert.True((await UpdateAsync(id, Input(name: "Second", transport: "RestApi", defaultDeviceId: device))).Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Null(db.AttendanceIntegrations.Single(i => i.Id == id).DefaultDeviceId);
    }

    private static IntegrationInput Input(int providerId = 0, string name = "HQ clock", string? reference = null, string? cron = null, string status = "Active", string transport = "FileUpload", string? zone = null, int? defaultDeviceId = null) => new()
    {
        DefaultDeviceId = defaultDeviceId,
        TimeZoneId = zone,
        ProviderId = providerId,
        DisplayName = name,
        ConnectionConfigRef = reference,
        PollingScheduleCron = cron,
        Status = status,
        Transport = transport,
    };

    private DeviceInput Device(string external, string name = "Front door", string zone = "UTC", string type = "Biometric") => new()
    {
        ExternalDeviceId = external,
        Name = name,
        TimeZoneId = zone,
        DeviceTypeId = _deviceTypeIds[type],
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

    private async Task<robeem.Application.Common.SaveResult> AddMappingAsync(int integrationId, int deviceId, MappingInput input)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).AddMappingAsync(integrationId, deviceId, input, CancellationToken.None);
    }

    private async Task<robeem.Application.Common.SaveResult> DeleteMappingAsync(int integrationId, int deviceId, int mappingId)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).DeleteMappingAsync(integrationId, deviceId, mappingId, CancellationToken.None);
    }

    private AttendanceIntegrationService CreateService(RobeemDbContext db) =>
        new(db, _sync, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), new ServiceCollection().BuildServiceProvider(), new StubAdapterCatalog());

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
