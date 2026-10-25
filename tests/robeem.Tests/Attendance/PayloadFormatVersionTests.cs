using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// A change to how a format reads a file, made once punches were read by it, is a new version: the old one stays, and a punch that
/// is normalized again later is read the way it was first read.
/// </summary>
public sealed class PayloadFormatVersionTests : AttendanceEngineTestBase
{
    [Fact]
    public async Task AFormatNoPunchWasReadBy_IsEditedInPlace()
    {
        var id = (await Create(Input())).Id!.Value;

        var changed = Input();
        changed.TimestampFormats = "MM/dd/yyyy HH:mm";
        var result = await Update(id, changed);

        Assert.Equal(id, result.Id);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var format = db.AttendancePayloadFormats.Single();
        Assert.Equal((1, "MM/dd/yyyy HH:mm", (int?)null), (format.Version, format.TimestampFormats, format.SupersedesId));
    }

    [Fact]
    public async Task ChangingHowAUsedFormatReadsFiles_IsANewVersion_TheOldOneIsKept_AndWhatUsedItMovesOn()
    {
        var oldId = (await Create(Input())).Id!.Value;
        UseFormat(oldId);

        var changed = Input();
        changed.TimestampFormats = "MM/dd/yyyy HH:mm";
        var result = await Update(oldId, changed);

        Assert.True(result.Succeeded, string.Join(" ", result.Errors.Values));
        Assert.NotEqual(oldId, result.Id);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var old = db.AttendancePayloadFormats.Single(f => f.Id == oldId);
        var next = db.AttendancePayloadFormats.Single(f => f.Id == result.Id);
        Assert.Equal(("dd/MM/yyyy HH:mm", 1), (old.TimestampFormats, old.Version));
        Assert.Equal(("MM/dd/yyyy HH:mm", 2, oldId), (next.TimestampFormats, next.Version, next.SupersedesId));
        Assert.Equal(next.Id, db.AttendanceIntegrations.Single().PayloadFormatId);
        Assert.Equal(next.Id, db.AttendanceConnectorDefinitions.Single().DefaultPayloadFormatId);

        // The event codes were copied to the new version, and only the new one is offered.
        Assert.Equal(["IN"], db.AttendancePayloadEventCodes.Where(c => c.PayloadFormatId == next.Id).Select(c => c.Code));
        var formats = new AttendancePayloadFormatService(db, CreateAudit(db), new CustomFormatFileReader(), new CustomFormatInterpreterFactory());
        Assert.Equal([next.Id], (await formats.ListAsync(CancellationToken.None)).Select(f => f.Id));
        Assert.Equal([next.Id], (await formats.ListOptionsAsync(CancellationToken.None)).Select(f => f.Id));
        Assert.Equal(2, (await formats.ListAsync(CancellationToken.None)).Single().Version);

        // The old version can still be read, but not edited.
        Assert.NotNull(await formats.GetInputAsync(oldId, CancellationToken.None));
        Assert.Contains("id", (await formats.UpdateAsync(oldId, changed, CancellationToken.None)).Errors.Keys);
    }

    [Fact]
    public async Task ChangesThatDoNotAffectReading_AreMadeInPlace_EvenForAUsedFormat()
    {
        var id = (await Create(Input())).Id!.Value;
        UseFormat(id);

        var described = Input();
        described.Description = "Now with a description";
        described.Name = "Renamed";
        described.EventCodes = [new PayloadFormatEventCodeInput { Code = " in ", EventType = "CheckIn" }];
        var result = await Update(id, described);

        Assert.Equal(id, result.Id);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var format = db.AttendancePayloadFormats.Single();
        Assert.Equal(("Renamed", 1), (format.Name, format.Version));
    }

    [Fact]
    public async Task AVersionedFormatCanBeRenamed_ButNotToAnotherFormatsName()
    {
        var firstId = (await Create(Input("First"))).Id!.Value;
        await Create(Input("Second"));
        UseFormat(firstId);
        var changed = Input("First");
        changed.TimestampFormats = "MM/dd/yyyy HH:mm";
        var secondVersion = (await Update(firstId, changed)).Id!.Value;

        var sameName = Input("First");
        sameName.TimestampFormats = "yyyy-MM-dd HH:mm";
        var again = await Update(secondVersion, sameName);
        var taken = Input("Second");
        taken.TimestampFormats = "yyyy-MM-dd HH:mm";

        Assert.True(again.Succeeded, string.Join(" ", again.Errors.Values));
        Assert.Contains(nameof(PayloadFormatInput.Name), (await Update(again.Id!.Value, taken)).Errors.Keys);
    }

    [Fact]
    public async Task APunchNormalizedAgain_IsReadByTheVersionItsBatchWasReadBy_NotByTheNewestOne()
    {
        var oldId = (await Create(Input())).Id!.Value;
        var batchId = AddBatchWithPendingPunch(oldId, "13/10/2026 08:00");
        var changed = Input();
        changed.TimestampFormats = "MM/dd/yyyy HH:mm";
        var newId = (await Update(oldId, changed)).Id!.Value;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceIntegrations.Single().PayloadFormatId = newId;
            db.SaveChanges();
        }

        NormalizationResult result;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var services = new ServiceCollection().AddSingleton<ICustomFormatInterpreterFactory, CustomFormatInterpreterFactory>().BuildServiceProvider();
            result = await new NormalizationService(db, new NormalizedTransactionRecorder(db), new AttendancePolicyResolver(db), services, NullLogger<NormalizationService>.Instance)
                .NormalizeBatchAsync(batchId, CancellationToken.None);
        }

        // "13/10/2026" is a day-first date: only the version it was read by understands it.
        Assert.Equal(1, result.Normalized);
        Assert.Equal(0, result.Failed);
        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(new DateTime(2026, 10, 13, 8, 0, 0), check.NormalizedAttendanceTransactions.Single(n => n.RawTransactionId == check.RawAttendanceTransactions.Single(r => r.ImportBatchId == batchId).Id).EventTimestampLocal);
    }

    private static PayloadFormatInput Input(string name = "Branch export") => new()
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
        return await new AttendancePayloadFormatService(db, CreateAudit(db), new CustomFormatFileReader(), new CustomFormatInterpreterFactory()).CreateAsync(input, CancellationToken.None);
    }

    private async Task<robeem.Application.Common.SaveResult> Update(int id, PayloadFormatInput input)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new AttendancePayloadFormatService(db, CreateAudit(db), new CustomFormatFileReader(), new CustomFormatInterpreterFactory()).UpdateAsync(id, input, CancellationToken.None);
    }

    /// <summary>Points the integration and its connector at the format and records an upload batch it read.</summary>
    private void UseFormat(int formatId)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        integration.PayloadFormatId = formatId;
        db.AttendanceConnectorDefinitions.Single().DefaultPayloadFormatId = formatId;
        db.AttendanceProcessingBatches.Add(new AttendanceProcessingBatch { IntegrationId = integration.Id, BatchType = AttendanceBatchType.ManualUpload, PayloadFormatId = formatId });
        db.SaveChanges();
    }

    private int AddBatchWithPendingPunch(int formatId, string timestamp)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        db.AttendanceConnections.Add(new AttendanceConnection { IntegrationId = integration.Id, Name = "Default", IsDefault = true });
        var batch = new AttendanceProcessingBatch { IntegrationId = integration.Id, BatchType = AttendanceBatchType.ManualUpload, PayloadFormatId = formatId };
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
