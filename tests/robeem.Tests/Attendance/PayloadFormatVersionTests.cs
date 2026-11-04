using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;

namespace robeem.Tests.Attendance;

/// <summary>
/// A change to how a format reads a file is a new version: the old one stays, and a punch that is normalized again later is read the
/// way it was first read. The catalog cannot see which tenants' punches a format read, so every such change is a new version.
/// Integrations refer to a format by name and read by its latest version, so a new version moves them on without touching them.
/// </summary>
public sealed class PayloadFormatVersionTests : AttendanceEngineTestBase
{
    private const string FormatName = "Branch export";

    [Fact]
    public async Task ChangingHowAFormatReadsFiles_IsANewVersion_TheOldOneIsKept_AndWhatNamesItReadsTheNewOne()
    {
        var oldId = (await Create(Input())).Id!.Value;
        UseFormat(oldId);

        var changed = Input();
        changed.TimestampFormats = "MM/dd/yyyy HH:mm";
        var result = await Update(oldId, changed);

        Assert.True(result.Succeeded, string.Join(" ", result.Errors.Values));
        Assert.NotEqual(oldId, result.Id);
        using var catalog = Database.CreateCatalog();
        var old = catalog.AttendancePayloadFormats.Single(f => f.Id == oldId);
        var next = catalog.AttendancePayloadFormats.Single(f => f.Id == result.Id);
        Assert.Equal(("dd/MM/yyyy HH:mm", 1), (old.TimestampFormats, old.Version));
        Assert.Equal(("MM/dd/yyyy HH:mm", 2, oldId), (next.TimestampFormats, next.Version, next.SupersedesId));

        // The connector's default moved on in the catalog; the tenant's integrations name no format, so they read its latest version.
        Assert.Equal(next.Id, catalog.AttendanceConnectorDefinitions.Single().DefaultPayloadFormatId);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var latest = await Database.CreateCatalogReader().GetLatestFormatAsync(FormatName, CancellationToken.None);
        Assert.Equal((next.Id, 2), (latest!.Id, latest.Version));

        // The event codes were copied to the new version, and only the new one is offered.
        Assert.Equal(["IN"], catalog.AttendancePayloadEventCodes.Where(c => c.PayloadFormatId == next.Id).Select(c => c.Code));
        var formats = Database.PayloadFormatService(db);
        Assert.Equal([next.Id], (await formats.ListAsync(CancellationToken.None)).Select(f => f.Id));
        Assert.Equal([next.Id], (await formats.ListOptionsAsync(CancellationToken.None)).Select(f => f.Id));
        Assert.Equal(2, (await formats.ListAsync(CancellationToken.None)).Single().Version);

        // The old version can still be read, but not edited.
        Assert.NotNull(await formats.GetInputAsync(oldId, CancellationToken.None));
        Assert.Contains("id", (await formats.UpdateAsync(oldId, changed, CancellationToken.None)).Errors.Keys);
    }

    [Fact]
    public async Task ChangesThatDoNotAffectReading_AreMadeInPlace()
    {
        var id = (await Create(Input())).Id!.Value;
        UseFormat(id);

        var described = Input();
        described.Description = "Now with a description";
        described.IsActive = false;
        described.EventCodes = [new PayloadFormatEventCodeInput { Code = " in ", EventType = "CheckIn" }];
        var result = await Update(id, described);

        Assert.Equal(id, result.Id);
        using var catalog = Database.CreateCatalog();
        var format = catalog.AttendancePayloadFormats.Single();
        Assert.Equal(("Now with a description", 1, false), (format.Description, format.Version, format.IsActive));
    }

    [Fact]
    public async Task TheNameOfAFormat_IsItsKey_AndCanNotBeChanged()
    {
        var firstId = (await Create(Input("First"))).Id!.Value;
        await Create(Input("Second"));

        var renamed = Input("Renamed");
        var taken = Input("Second");

        // Integrations in every tenant refer to the format by its name, so it stays; a new format is made under a new name.
        Assert.Contains(nameof(PayloadFormatInput.Name), (await Update(firstId, renamed)).Errors.Keys);
        Assert.Contains(nameof(PayloadFormatInput.Name), (await Update(firstId, taken)).Errors.Keys);
        Assert.Contains(nameof(PayloadFormatInput.Name), (await Create(Input("SECOND"))).Errors.Keys);
    }

    [Fact]
    public async Task APunchNormalizedAgain_IsReadByTheVersionItsBatchWasReadBy_NotByTheNewestOne()
    {
        var oldId = (await Create(Input())).Id!.Value;
        var batchId = AddBatchWithPendingPunch("13/10/2026 08:00");
        var changed = Input();
        changed.TimestampFormats = "MM/dd/yyyy HH:mm";
        await Update(oldId, changed);
        using (var catalog = Database.CreateCatalog())
        {
            // The connector's default is the newest version now, but the batch recorded the one it was read by.
            catalog.AttendanceConnectorDefinitions.Single().DefaultPayloadFormatId = catalog.AttendancePayloadFormats.Single(f => f.Version == 2).Id;
            catalog.SaveChanges();
        }

        NormalizationResult result;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var services = new ServiceCollection().AddSingleton<ICustomFormatInterpreterFactory, CustomFormatInterpreterFactory>().BuildServiceProvider();
            result = await new NormalizationService(db, Database.CreateCatalogReader(), new NormalizedTransactionRecorder(db), new AttendancePolicyResolver(db), services, NullLogger<NormalizationService>.Instance)
                .NormalizeBatchAsync(batchId, CancellationToken.None);
        }

        // "13/10/2026" is a day-first date: only the version it was read by understands it.
        Assert.Equal(1, result.Normalized);
        Assert.Equal(0, result.Failed);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(new DateTime(2026, 10, 13, 8, 0, 0), check.NormalizedAttendanceTransactions.Single(n => n.RawTransactionId == check.RawAttendanceTransactions.Single(r => r.ImportBatchId == batchId).Id).EventTimestampLocal);
    }

    private static PayloadFormatInput Input(string name = FormatName) => new()
    {
        Name = name,
        EmployeeColumn = "Badge",
        TimestampColumn = "Time",
        TypeColumn = "Type",
        TimestampFormats = "dd/MM/yyyy HH:mm",
        EventCodes = [new PayloadFormatEventCodeInput { Code = "IN", EventType = "CheckIn" }],
    };

    private async Task<robeem.Application.Common.SaveResult> Create(PayloadFormatInput input)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Database.PayloadFormatService(db).CreateAsync(input, CancellationToken.None);
    }

    private async Task<robeem.Application.Common.SaveResult> Update(int id, PayloadFormatInput input)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await Database.PayloadFormatService(db).UpdateAsync(id, input, CancellationToken.None);
    }

    /// <summary>Points the connector (in the catalog) at the format and records an upload batch it read.</summary>
    private void UseFormat(int formatId)
    {
        using (var catalog = Database.CreateCatalog())
        {
            catalog.AttendanceConnectorDefinitions.Single().DefaultPayloadFormatId = formatId;
            catalog.SaveChanges();
        }

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        db.AttendanceProcessingBatches.Add(new AttendanceProcessingBatch { IntegrationId = integration.Id, BatchType = AttendanceBatchType.ManualUpload, PayloadFormatName = FormatName, PayloadFormatVersion = 1 });
        db.SaveChanges();
    }

    private int AddBatchWithPendingPunch(string timestamp)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        db.AttendanceConnections.Add(new AttendanceConnection { IntegrationId = integration.Id, Name = "Default", IsDefault = true });
        var batch = new AttendanceProcessingBatch { IntegrationId = integration.Id, BatchType = AttendanceBatchType.ManualUpload, PayloadFormatName = FormatName, PayloadFormatVersion = 1 };
        db.AttendanceProcessingBatches.Add(batch);
        db.RawAttendanceTransactions.Add(new RawAttendanceTransaction
        {
            IntegrationId = integration.Id,
            ImportBatch = batch,
            TransactionTimestampRaw = timestamp,
            TransactionTypeRaw = "IN",
            DeviceExternalId = "DEV-1",
            ExternalEmployeeId = "E-1",
            RawPayload = "{}",
            SourceSystem = "generic-csv",
            RecordHash = Guid.NewGuid().ToString("N"),
        });
        db.SaveChanges();
        return batch.Id;
    }
}
