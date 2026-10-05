using robeem.DataModel;

namespace robeem.Tests.Infrastructure.Attendance;

/// <summary>The audit log is insert-only: the DbContext refuses to change or delete a row (P12).</summary>
public sealed class AttendanceAuditLogTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AnEntry_CanNotBeChanged_OrDeleted()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceAuditLogs.Add(new AttendanceAuditLog { Actor = "system", Action = "Test", TargetType = "Thing", TargetId = 1, Details = "first" });
            await db.SaveChangesAsync();
        }

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceAuditLogs.Single().Details = "rewritten";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceAuditLogs.Remove(db.AttendanceAuditLogs.Single());
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal("first", check.AttendanceAuditLogs.Single().Details);
    }

    [Fact]
    public async Task AnEntry_IsStampedWithItsTenant_AndInvisibleToAnother()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceAuditLogs.Add(new AttendanceAuditLog { Actor = "system", Action = "Test", TargetType = "Thing" });
            await db.SaveChangesAsync();
            Assert.Equal(TestDatabase.TenantA, db.AttendanceAuditLogs.Single().TenantId);
        }

        using var other = _database.CreateContext(TestDatabase.TenantB);
        Assert.Empty(other.AttendanceAuditLogs);
    }
}
