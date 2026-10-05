using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>The attendance audit trail (P12): what is recorded, who is the actor, and that it can't be rewritten.</summary>
public sealed class AttendanceAuditTrailTests : AttendanceEngineTestBase
{
    [Fact]
    public async Task ARaisedException_IsAudited_WithItsId_AndAnAutoResolvedOneToo()
    {
        Clock.Local = Monday.At(18, 0);
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        await ProcessAsync(Monday);

        int exceptionId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            exceptionId = Assert.Single(db.AttendanceExceptions).Id;
        }

        var raised = Assert.Single(await ListAsync(AttendanceAuditActions.ExceptionStatusChanged));
        Assert.Equal(("system", nameof(AttendanceException), exceptionId), (raised.Actor, raised.EntityType, raised.EntityId));
        Assert.StartsWith("(new) -> Open", raised.Details);

        // The missing punch turns up late: the problem is gone and the engine closes the exception.
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        await ProcessAsync(Monday);

        var changes = (await ListAsync(AttendanceAuditActions.ExceptionStatusChanged)).OrderBy(e => e.Id).ToList();
        Assert.Equal(2, changes.Count);
        Assert.Equal(("system", exceptionId), (changes[1].Actor, changes[1].EntityId));
        Assert.StartsWith("Open -> Closed", changes[1].Details);
        Assert.Contains("auto-resolved", changes[1].Details);
    }

    [Fact]
    public async Task TheHealthReport_ReadsRawAndNormalizedAttendance_AndIsAuditedForTheUser()
    {
        int integrationId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            integrationId = db.AttendanceIntegrations.Single().Id;
        }

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var service = new IntegrationHealthService(
                db,
                new IntegrationResilience(Options.Create(new AttendanceSyncOptions()), Clock),
                Options.Create(new AttendanceSyncOptions()),
                CreateAudit(db, new TestUser(77)),
                Clock);
            await service.GetIntegrationHealthAsync(integrationId, CancellationToken.None);
        }

        var read = Assert.Single(await ListAsync(AttendanceAuditActions.AttendanceDataRead));
        Assert.Equal("employee:77", read.Actor);
        Assert.Contains(nameof(RawAttendanceTransaction), read.EntityType);
        Assert.Contains(nameof(NormalizedAttendanceTransaction), read.EntityType);
        Assert.Contains($"integration {integrationId}", read.Details);
    }

    [Fact]
    public async Task TheTrail_IsListedNewestFirst_Filtered_AndPerTenant()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var audit = CreateAudit(db);
            audit.Record("A", "Thing", 1, "one");
            audit.Record("B", "Thing", 2, "two");
            audit.Record("A", "Other", 3, "three");
            await db.SaveChangesAsync();
        }

        using (var other = Database.CreateContext(TestDatabase.TenantB))
        {
            CreateAudit(other).Record("A", "Thing", 1, "another tenant's");
            await other.SaveChangesAsync();
        }

        Assert.Equal(["three", "one"], (await ListAsync("A")).Select(e => e.Details));
        using var db2 = Database.CreateContext(TestDatabase.TenantA);
        var audit2 = CreateAudit(db2);
        Assert.Equal(["three", "two", "one"], (await audit2.ListAsync(null, null, null, 100, CancellationToken.None)).Select(e => e.Details));
        Assert.Equal(["two"], (await audit2.ListAsync(null, "Thing", 2, 100, CancellationToken.None)).Select(e => e.Details));
        Assert.Single(await audit2.ListAsync(null, null, null, 1, CancellationToken.None));
    }

    [Fact]
    public async Task TheActor_IsTheCurrentUser_UnlessTheCallerNamesOne()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var audit = CreateAudit(db, new TestUser(5));
            audit.Record("A", "Thing", 1);
            audit.Record("A", "Thing", 2, actor: "employee:9");
            await db.SaveChangesAsync();
        }

        Assert.Equal(["employee:9", "employee:5"], (await ListAsync("A")).Select(e => e.Actor));
    }

    [Fact]
    public async Task ALongDetail_IsTruncated_NotRefused()
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            CreateAudit(db).Record("A", "Thing", 1, new string('x', 5000));
            await db.SaveChangesAsync();
        }

        Assert.Equal(2000, Assert.Single(await ListAsync("A")).Details!.Length);
    }

    private async Task<IReadOnlyList<robeem.Contracts.Attendance.AuditLogEntry>> ListAsync(string action)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await CreateAudit(db).ListAsync(action, null, null, 100, CancellationToken.None);
    }
}
