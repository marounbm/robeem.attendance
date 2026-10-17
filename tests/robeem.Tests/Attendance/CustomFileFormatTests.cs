using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// Custom file formats: defining one, reading an upload by it, and normalizing what it read. Also
/// manual upload for a provider that has no file reader of its own (a REST one).
/// </summary>
public sealed class CustomFileFormatTests : IDisposable
{
    private const string RestKey = "rest-only";

    // No header row; semicolons; date and time in separate columns; day first.
    private const string SemicolonFile =
        "E-1;04/10/2026;09:00;I;DEV-1\r\n" +
        "E-1;04/10/2026;17:30;O;DEV-1\r\n" +
        "E-2;04/10/2026;09:05;X;DEV-1\r\n";

    private readonly TestDatabase _database = new();
    private readonly RestStub _rest = new();
    private readonly ServiceProvider _services;
    private readonly string _path = Path.GetTempFileName();
    private readonly int _integrationId;
    private readonly int _deviceId;
    private readonly int _employeeId;

    public CustomFileFormatTests()
    {
        _services = new ServiceCollection()
            .AddKeyedSingleton<IAttendanceProvider>(RestKey, _rest)
            .AddSingleton<ICustomFormatFileReader, CustomFormatFileReader>()
            .AddSingleton<ICustomFormatInterpreterFactory, CustomFormatInterpreterFactory>()
            .BuildServiceProvider();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var provider = new AttendanceProvider { ProviderKey = RestKey, DisplayName = "REST only" };
        provider.Transports.Add(new AttendanceProviderTransport { Transport = new AttendanceTransportDefinition { TransportKey = nameof(AttendanceTransport.RestApi), DisplayName = "REST API", IsPull = true } });
        db.Add(new AttendanceTransportDefinition { TransportKey = nameof(AttendanceTransport.FileUpload), DisplayName = "Manual file upload", UsesFileFormat = true });
        var employee = new Profile { EmployeeNumber = "P-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        var integration = new AttendanceIntegration { Provider = provider, DisplayName = "Branch upload", Transport = AttendanceTransport.FileUpload };
        var device = new AttendanceDevice { Integration = integration, ExternalDeviceId = "DEV-1", Name = "Door", TimeZoneId = "Asia/Beirut" };
        db.AddRange(device, new ExternalEmployeeIdMapping { Integration = integration, ExternalEmployeeId = "E-1", Employee = employee });
        db.SaveChanges();
        (_integrationId, _deviceId, _employeeId) = (integration.Id, device.Id, employee.Id);
    }

    public void Dispose()
    {
        File.Delete(_path);
        _services.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task AFormat_IsCreated_Listed_AndReadBackAsInput()
    {
        var created = await CreateFormatAsync(SemicolonFormat());

        Assert.True(created.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = FormatService(db);
        var list = await service.ListAsync(CancellationToken.None);
        var item = Assert.Single(list);
        Assert.Equal(("Branch export", "Auto", "semicolons", 2, 0, true), (item.Name, item.FileKind, item.Delimiter, item.EventCodeCount, item.IntegrationCount, item.IsActive));
        Assert.Equal(["Branch export"], (await service.ListOptionsAsync(CancellationToken.None)).Select(o => o.Name));

        var input = (await service.GetInputAsync(created.Id!.Value, CancellationToken.None))!;
        Assert.Equal((";", false, "1", "2", "3", "dd/MM/yyyy HH:mm"), (input.Delimiter, input.HasHeaderRow, input.EmployeeColumn, input.DateColumn, input.TimeColumn, input.TimestampFormats));
        Assert.Equal(["I", "O"], input.EventCodes.Select(c => c.Code));
        Assert.Null(await service.GetInputAsync(999, CancellationToken.None));
    }

    [Fact]
    public async Task ChangingAFormat_KeepsUpdatesAddsAndDropsItsLabels_AndIsAudited()
    {
        var id = (await CreateFormatAsync(SemicolonFormat())).Id!.Value;
        var changed = SemicolonFormat();
        changed.EventCodes =
        [
            new FileFormatEventCodeInput { Code = "i", EventType = "BreakIn" },
            new FileFormatEventCodeInput { Code = "LUNCH OUT", EventType = "BreakOut" },
        ];

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var result = await FormatService(db).UpdateAsync(id, changed, CancellationToken.None);

        Assert.True(result.Succeeded);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        var codes = check.AttendanceFileFormatEventCodes.OrderBy(c => c.Code).Select(c => new { c.Code, c.EventType }).ToList();
        Assert.Equal([("LUNCH OUT", NormalizedEventType.BreakOut), ("i", NormalizedEventType.BreakIn)], codes.Select(c => (c.Code, c.EventType)));
        Assert.Equal(2, check.AttendanceAuditLogs.Count(l => l.Action == AttendanceAuditActions.IntegrationChanged));
        Assert.True((await FormatService(check).UpdateAsync(999, changed, CancellationToken.None)).NotFound);
    }

    [Theory]
    [InlineData("Name", "", "required")]
    [InlineData("Delimiter", "#", "delimiter")]
    [InlineData("FileKind", "Pdf", "file kind")]
    [InlineData("SheetNumber", "0", "between 1 and 50")]
    [InlineData("EmployeeColumn", "", "required")]
    [InlineData("EmployeeColumn", "Emp", "column's number")]
    [InlineData("DeviceColumn", "0", "column's number")]
    [InlineData("TypeColumn", "9999999999999", "column's number")]
    [InlineData("TimestampFormats", "", "at least one")]
    [InlineData("TimestampFormats", "d", "not a usable pattern")]
    [InlineData("TimestampFormats", "dd/MM/yyyy", "not a usable pattern")]
    [InlineData("TimestampFormats", "banana banana", "not a usable pattern")]
    public async Task ABadFormat_IsRefused(string field, string value, string fragment)
    {
        var input = SemicolonFormat();
        typeof(FileFormatInput).GetProperty(field)!.SetValue(input, field == "SheetNumber" ? int.Parse(value) : value);

        var result = await CreateFormatAsync(input);

        Assert.Contains(fragment, result.Errors[field]);
    }

    [Fact]
    public async Task TheTimestamp_IsOneColumnOrADateAndATime_NeverBoth_NeverNeither()
    {
        var both = SemicolonFormat();
        both.TimestampColumn = "2";
        var neither = SemicolonFormat();
        neither.DateColumn = null;
        neither.TimeColumn = null;

        Assert.Contains("not both", (await CreateFormatAsync(both)).Errors[nameof(FileFormatInput.TimestampColumn)]);
        Assert.Contains("Give the timestamp column", (await CreateFormatAsync(neither)).Errors[nameof(FileFormatInput.TimestampColumn)]);
    }

    [Theory]
    [InlineData("Fingerprint template")]
    [InlineData("FaceTemplate")]
    [InlineData("Biometric data")]
    public async Task ABiometricColumn_IsNeverReadByAFormat(string column)
    {
        var input = SemicolonFormat();
        input.HasHeaderRow = true;
        input.EmployeeColumn = "Employee";
        input.DateColumn = "Date";
        input.TimeColumn = "Time";
        input.TypeColumn = column;
        input.DeviceColumn = null;

        Assert.Contains("biometric", (await CreateFormatAsync(input)).Errors[nameof(FileFormatInput.TypeColumn)]);
    }

    [Fact]
    public async Task TheLabels_MustBeKnownEventTypes_AndDistinctOnceNormalized()
    {
        var unknown = SemicolonFormat();
        unknown.EventCodes = [new FileFormatEventCodeInput { Code = "I", EventType = "Lunch" }];
        var duplicate = SemicolonFormat();
        duplicate.EventCodes = [new FileFormatEventCodeInput { Code = "Check-In", EventType = "CheckIn" }, new FileFormatEventCodeInput { Code = "check in", EventType = "CheckIn" }];

        Assert.Contains("event type must be one of", (await CreateFormatAsync(unknown)).Errors[nameof(FileFormatInput.EventCodes)]);
        Assert.Contains("the same", (await CreateFormatAsync(duplicate)).Errors[nameof(FileFormatInput.EventCodes)]);
    }

    [Fact]
    public async Task TheNameOfAFormat_IsUnique_IgnoringCase()
    {
        await CreateFormatAsync(SemicolonFormat());
        var other = SemicolonFormat();
        other.Name = "BRANCH EXPORT";

        Assert.Contains("already has this name", (await CreateFormatAsync(other)).Errors[nameof(FileFormatInput.Name)]);
    }

    [Fact]
    public async Task AnIntegrationOfAProviderWithNoFileReader_CanBeSetToManualUpload_AndPicksAFormat()
    {
        var formatId = (await CreateFormatAsync(SemicolonFormat())).Id!.Value;

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = IntegrationService(db);
        var providers = await service.ListProvidersAsync(CancellationToken.None);
        Assert.Equal([nameof(AttendanceTransport.RestApi), nameof(AttendanceTransport.FileUpload)], providers.Single(p => p.Key == RestKey).SupportedTransports.Select(t => t.Key));

        var result = await service.UpdateAsync(_integrationId, InputFor(db, "FileUpload", formatId), CancellationToken.None);

        Assert.True(result.Succeeded);
        var input = (await service.GetInputAsync(_integrationId, CancellationToken.None))!;
        Assert.Equal(("FileUpload", formatId), (input.Transport, input.FileFormatId));
        var item = (await service.GetAsync(_integrationId, CancellationToken.None))!;
        Assert.Equal("Branch export", item.FileFormatName);
    }

    [Fact]
    public async Task TheFormat_IsDroppedWhenTheIntegrationStopsBeingAnUpload_AndMustExistAndBeActiveToBeChosen()
    {
        var formatId = (await CreateFormatAsync(SemicolonFormat())).Id!.Value;
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = IntegrationService(db);

        Assert.Contains("does not exist", (await service.UpdateAsync(_integrationId, InputFor(db, "FileUpload", 999), CancellationToken.None)).Errors[nameof(IntegrationInput.FileFormatId)]);

        await service.UpdateAsync(_integrationId, InputFor(db, "FileUpload", formatId), CancellationToken.None);
        await service.UpdateAsync(_integrationId, InputFor(db, "RestApi", formatId), CancellationToken.None);

        Assert.Null((await service.GetInputAsync(_integrationId, CancellationToken.None))!.FileFormatId);
    }

    [Fact]
    public async Task ARetiredFormat_CanNotBeChosenAnew_ButAnIntegrationKeepsItsOwn()
    {
        var input = SemicolonFormat();
        var formatId = (await CreateFormatAsync(input)).Id!.Value;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            await IntegrationService(db).UpdateAsync(_integrationId, InputFor(db, "FileUpload", formatId), CancellationToken.None);
        }

        input.IsActive = false;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            await FormatService(db).UpdateAsync(formatId, input, CancellationToken.None);
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var service = IntegrationService(check);
        Assert.Empty(await FormatService(check).ListOptionsAsync(CancellationToken.None));
        Assert.True((await service.UpdateAsync(_integrationId, InputFor(check, "FileUpload", formatId), CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task APreview_ShowsHowEachRowIsUnderstood_AndStoresNothing()
    {
        File.WriteAllText(_path, SemicolonFile);
        var format = SemicolonFormat();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var preview = await FormatService(db).PreviewAsync(format, _path, CancellationToken.None);

        Assert.Equal(string.Empty, preview.Problem);
        Assert.Equal(3, preview.TotalRows);
        Assert.Equal(["2026-10-04 09:00:00", "2026-10-04 17:30:00", "2026-10-04 09:05:00"], preview.Rows.Select(r => r.ParsedTimestamp));
        Assert.Equal(["CheckIn", "CheckOut", null], preview.Rows.Select(r => r.EventType));
        Assert.Equal(string.Empty, preview.Rows[0].Problem);
        Assert.Contains("the label 'X' is not mapped", preview.Rows[2].Problem);
        Assert.Empty(db.AttendanceFileFormats);
        Assert.Empty(db.RawAttendanceTransactions);
    }

    [Fact]
    public async Task APreview_SaysWhyAFileOrAFormatCanNotBeRead()
    {
        File.WriteAllText(_path, SemicolonFile);
        var noSuchColumn = SemicolonFormat();
        noSuchColumn.DeviceColumn = "9";
        var invalid = SemicolonFormat();
        invalid.Name = string.Empty;
        var excel = SemicolonFormat();
        excel.FileKind = "Excel";

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = FormatService(db);

        Assert.Contains("no column 9", (await service.PreviewAsync(noSuchColumn, _path, CancellationToken.None)).Problem);
        Assert.Contains("not valid yet", (await service.PreviewAsync(invalid, _path, CancellationToken.None)).Problem);
        Assert.Contains("not an Excel workbook", (await service.PreviewAsync(excel, _path, CancellationToken.None)).Problem);
    }

    [Fact]
    public async Task AHeaderedFile_IsReadByColumnName_IgnoringCaseAndPunctuation_AndRowsKeepEveryColumn()
    {
        File.WriteAllText(_path, "Badge No.|Punch Date|Punch Time|Fingerprint Template\r\n E-1 |04/10/2026|09:00|AAAA\r\n");
        var format = SemicolonFormat();
        format.Delimiter = "|";
        format.HasHeaderRow = true;
        format.EmployeeColumn = "badge no";
        format.DateColumn = "PUNCH DATE";
        format.TimeColumn = "punch_time";
        format.TypeColumn = null;
        format.DeviceColumn = null;

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var preview = await FormatService(db).PreviewAsync(format, _path, CancellationToken.None);

        var row = Assert.Single(preview.Rows);
        Assert.Equal(("E-1", "2026-10-04 09:00:00"), (row.ExternalEmployeeId, row.ParsedTimestamp));
    }

    [Fact]
    public async Task ARestProvidersIntegration_SetToUploadWithNoFormat_IsATarget_ThatSaysToChooseAFormat()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var target = Assert.Single(await IngestionService(db).ListUploadTargetsAsync(CancellationToken.None));

        Assert.Equal((RestKey, "Branch upload"), (target.ProviderKey, target.DisplayName));
        Assert.Empty(target.FileFormats);
        Assert.Contains("choose a file format", target.Layout.Summary);
        Assert.Equal(RestKey, target.Layout.ProviderKey);
    }

    [Fact]
    public async Task AnUploadToAProviderWithNoFormat_FailsTheBatch_WithTheReason_AndTheRestApiIsNeverCalled()
    {
        File.WriteAllText(_path, "employee_id,timestamp,type,device_id\r\nE-1,2026-10-04 09:00,IN,DEV-1\r\n");

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var result = await IngestionService(db).IngestManualUploadAsync(_integrationId, _path, CancellationToken.None);

        Assert.Equal(nameof(AttendanceBatchStatus.Failed), result!.Status);
        Assert.Contains("has no file format", result.ErrorSummary);
        Assert.Equal(0, _rest.Calls);
        Assert.Empty(db.RawAttendanceTransactions);
    }

    [Fact]
    public async Task AProvidersDefaultFormat_ReadsTheUploadsOfAnIntegrationThatNamesNone()
    {
        var formatId = (await CreateFormatAsync(SemicolonFormat())).Id!.Value;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceProviders.Single().DefaultFileFormatId = formatId;
            db.SaveChanges();
        }

        File.WriteAllText(_path, SemicolonFile);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        var target = Assert.Single(await IngestionService(check).ListUploadTargetsAsync(CancellationToken.None));
        var result = await IngestionService(check).IngestManualUploadAsync(_integrationId, _path, CancellationToken.None);

        Assert.Contains("Custom format 'Branch export'", target.Layout.Summary);
        Assert.Equal((nameof(AttendanceBatchStatus.Succeeded), 3, 0), (result!.Status, result.RecordsIngested, result.RecordsFailed));
        Assert.Equal(0, _rest.Calls);
    }

    [Fact]
    public async Task AnIntegrationWithAFormat_ShowsItsOwnLayout_AndReadsAndNormalizesTheUploadByIt()
    {
        var formatId = (await CreateFormatAsync(SemicolonFormat())).Id!.Value;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            await IntegrationService(db).UpdateAsync(_integrationId, InputFor(db, "FileUpload", formatId), CancellationToken.None);
        }

        File.WriteAllText(_path, SemicolonFile);
        AttendanceIngestionResult? ingested;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var target = Assert.Single(await IngestionService(db).ListUploadTargetsAsync(CancellationToken.None));
            Assert.Contains("Custom format 'Branch export'", target.Layout.Summary);
            Assert.Equal(["dd/MM/yyyy HH:mm"], target.Layout.TimestampFormats);
            Assert.Equal(["I", "O"], target.Layout.EventCodes.Select(c => c.Code));

            ingested = await IngestionService(db).IngestManualUploadAsync(_integrationId, _path, CancellationToken.None);
        }

        Assert.Equal((nameof(AttendanceBatchStatus.Succeeded), 3, 0), (ingested!.Status, ingested.RecordsIngested, ingested.RecordsFailed));
        Assert.Equal(0, _rest.Calls);

        using var normalizing = _database.CreateContext(TestDatabase.TenantA);
        var result = await new NormalizationService(
            normalizing,
            new NormalizedTransactionRecorder(normalizing),
            new AttendancePolicyResolver(normalizing),
            _services,
            NullLogger<NormalizationService>.Instance).NormalizeBatchAsync(ingested.BatchId, CancellationToken.None);

        // E-1's two punches are understood by the format's labels; E-2's "X" is a label the format does not map.
        Assert.Equal(2, result.Normalized);
        Assert.Equal(1, result.Failed);
        var rows = normalizing.NormalizedAttendanceTransactions.AsEnumerable().OrderBy(n => n.EventTimestampUtc).ToList();
        Assert.Equal([NormalizedEventType.CheckIn, NormalizedEventType.CheckOut], rows.Select(r => r.NormalizedEventType));
        Assert.All(rows, r => Assert.Equal(_employeeId, r.EmployeeId));
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 6, 0, 0, TimeSpan.Zero), rows[0].EventTimestampUtc);
        Assert.Contains("Unrecognized event type 'X'", normalizing.RawAttendanceTransactions.Single(r => r.ExternalEmployeeId == "E-2").ProcessingError);
    }

    [Fact]
    public async Task AFileThatIsNotWhatTheFormatSays_FailsTheBatch_WithTheReason()
    {
        var format = SemicolonFormat();
        format.FileKind = "Excel";
        var formatId = (await CreateFormatAsync(format)).Id!.Value;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            await IntegrationService(db).UpdateAsync(_integrationId, InputFor(db, "FileUpload", formatId), CancellationToken.None);
        }

        File.WriteAllText(_path, SemicolonFile);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        var result = await IngestionService(check).IngestManualUploadAsync(_integrationId, _path, CancellationToken.None);

        Assert.Equal(nameof(AttendanceBatchStatus.Failed), result!.Status);
        Assert.Contains("not an Excel workbook", result.ErrorSummary);
        Assert.Empty(check.RawAttendanceTransactions);
    }

    private static FileFormatInput SemicolonFormat() => new()
    {
        Name = "Branch export",
        Delimiter = ";",
        HasHeaderRow = false,
        EmployeeColumn = "1",
        DateColumn = "2",
        TimeColumn = "3",
        TypeColumn = "4",
        DeviceColumn = "5",
        TimestampFormats = "dd/MM/yyyy HH:mm",
        EventCodes =
        [
            new FileFormatEventCodeInput { Code = "I", EventType = "CheckIn" },
            new FileFormatEventCodeInput { Code = "O", EventType = "CheckOut" },
        ],
    };

    private static IntegrationInput InputFor(RobeemDbContext db, string transport, int? formatId)
    {
        var integration = db.AttendanceIntegrations.Single();
        return new IntegrationInput
        {
            ProviderId = integration.ProviderId,
            DisplayName = integration.DisplayName,
            Transport = transport,
            FileFormatId = formatId,
            Status = "Active",
        };
    }

    private async Task<robeem.Application.Common.SaveResult> CreateFormatAsync(FileFormatInput input)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await FormatService(db).CreateAsync(input, CancellationToken.None);
    }

    private AttendanceFileFormatService FormatService(RobeemDbContext db) =>
        new(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), new CustomFormatFileReader(), new CustomFormatInterpreterFactory());

    private AttendanceIntegrationService IntegrationService(RobeemDbContext db) =>
        new(db, new NoSync(), new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), _services, new StubAdapterCatalog());

    private IngestionService IngestionService(RobeemDbContext db) =>
        new(db, new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), _services);

    private sealed class RestStub : IAttendanceProvider
    {
        public int Calls { get; private set; }

        public Task<ConnectionValidationResult> ValidateConnectionAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw Called();

        public Task<AuthResult> AuthenticateAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw Called();

        public Task<IReadOnlyList<ExternalDeviceDto>> RetrieveDevicesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw Called();

        public Task<IReadOnlyList<ExternalEmployeeDto>> RetrieveEmployeesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw Called();

        public Task<AttendanceBatchResult> RetrieveAttendanceTransactionsAsync(AttendanceIntegrationConfig config, DateRange? range, CancellationToken cancellationToken) => throw Called();

        public Task<AttendanceBatchResult> GetTransactionChangesAsync(AttendanceIntegrationConfig config, SyncCursor? cursor, CancellationToken cancellationToken) => throw Called();

        private Exception Called()
        {
            Calls++;
            return new InvalidOperationException("The REST API must not be called for an upload.");
        }
    }

    private sealed class NoSync : IIntegrationSyncService
    {
        public Task<IReadOnlyList<PollingIntegration>> ListPollingIntegrationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> SyncAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> ReconcileAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> PruneEmptyBatchesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
