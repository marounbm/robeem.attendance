using Microsoft.EntityFrameworkCore;
using robeem.DataModel;

namespace robeem.Tests.Infrastructure.Attendance;

public sealed class RawAttendanceTransactionTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public void Insert_StoresTheRow_WithTenantStampedAndInstantsInUtc()
    {
        var punch = new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.FromHours(3));
        InsertRaw(punch);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var raw = db.RawAttendanceTransactions.Include(r => r.ImportBatch).Single();
        Assert.Equal(TestDatabase.TenantA, raw.TenantId);
        Assert.Equal("2026-10-04T09:00:00+03:00", raw.TransactionTimestampRaw);
        Assert.Equal(punch, raw.TransactionTimestampParsed);
        Assert.Equal(TimeSpan.Zero, raw.TransactionTimestampParsed!.Value.Offset);
        Assert.Equal(RawTransactionProcessingStatus.Pending, raw.ProcessingStatus);
        Assert.Equal(AttendanceBatchType.ManualUpload, raw.ImportBatch!.BatchType);
    }

    [Fact]
    public void Update_OfAVendorField_Throws()
    {
        InsertRaw();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var raw = db.RawAttendanceTransactions.Single();
        raw.ExternalEmployeeId = "E-2";

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.StartsWith("RawAttendanceTransaction is immutable.", ex.Message);
        Assert.Contains(nameof(RawAttendanceTransaction.ExternalEmployeeId), ex.Message);
    }

    [Fact]
    public void Update_OfADetachedRow_Throws()
    {
        var id = InsertRaw();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var detached = new RawAttendanceTransaction
        {
            Id = id,
            TenantId = TestDatabase.TenantA,
            ProcessingStatus = RawTransactionProcessingStatus.Normalized,
        };
        db.Update(detached);

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void Delete_Throws()
    {
        InsertRaw();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        db.RawAttendanceTransactions.Remove(db.RawAttendanceTransactions.Single());

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal("RawAttendanceTransaction is immutable.", ex.Message);
    }

    [Fact]
    public void Update_OfBookkeepingColumns_IsAllowed()
    {
        InsertRaw();

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var raw = db.RawAttendanceTransactions.Single();
            raw.ProcessingStatus = RawTransactionProcessingStatus.Failed;
            raw.ProcessingError = "Unparseable timestamp.";
            raw.RetentionStatus = RawTransactionRetentionStatus.Archived;
            db.SaveChanges();
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var stored = check.RawAttendanceTransactions.Single();
        Assert.Equal(RawTransactionProcessingStatus.Failed, stored.ProcessingStatus);
        Assert.Equal(RawTransactionRetentionStatus.Archived, stored.RetentionStatus);
    }

    private int InsertRaw(DateTimeOffset? punch = null)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var integration = new AttendanceIntegration
        {
            Connector = AttendanceTestData.Connector("generic-csv", "Generic CSV"),
            DisplayName = "HQ",
        };
        var raw = new RawAttendanceTransaction
        {
            Integration = integration,
            ImportBatch = new AttendanceProcessingBatch { Integration = integration, BatchType = AttendanceBatchType.ManualUpload },
            ExternalEmployeeId = "E-1",
            TransactionTimestampRaw = "2026-10-04T09:00:00+03:00",
            TransactionTimestampParsed = punch,
            RawPayload = "{\"emp\":\"E-1\"}",
            SourceSystem = "generic-csv",
            RecordHash = "hash",
        };
        db.RawAttendanceTransactions.Add(raw);
        db.SaveChanges();
        return raw.Id;
    }
}
