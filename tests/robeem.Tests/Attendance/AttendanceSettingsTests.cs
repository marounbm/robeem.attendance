using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>The date the punching started: saving it, and what it holds back (punches before it, corrections for earlier days).</summary>
public sealed class AttendanceSettingsTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task TheStartDate_IsSavedOncePerTenant_ReadBack_AndAudited()
    {
        Assert.True((await SaveAsync(new DateTime(2026, 4, 1, 13, 0, 0))).Succeeded);
        Assert.True((await SaveAsync(new DateTime(2026, 5, 1))).Succeeded);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(new DateTime(2026, 5, 1), db.AttendanceSettings.Single().StartDate);
        Assert.Equal(new DateTime(2026, 5, 1), (await Service(db).GetAsync(CancellationToken.None)).StartDate);
        Assert.Equal(2, db.AttendanceAuditLogs.Count(a => a.Action == AttendanceAuditActions.AttendanceSettingsChanged));

        using var other = _database.CreateContext(TestDatabase.TenantB);
        Assert.Null((await Service(other).GetAsync(CancellationToken.None)).StartDate);
    }

    [Fact]
    public async Task TheStartDate_CanNotBeInTheFuture_AndSavingTheSameDateAgainChangesNothing()
    {
        var future = await SaveAsync(DateTime.Today.AddDays(2));
        Assert.Contains("future", future.Errors[nameof(AttendanceSettingsInput.StartDate)]);

        await SaveAsync(new DateTime(2026, 4, 1));
        await SaveAsync(new DateTime(2026, 4, 1));
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Single(db.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.AttendanceSettingsChanged));
    }

    internal static AttendanceSettingsService SettingsServiceFor(RobeemDbContext db) =>
        new(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), TimeProvider.System);

    private AttendanceSettingsService Service(RobeemDbContext db) => SettingsServiceFor(db);

    private async Task<robeem.Application.Common.SaveResult> SaveAsync(DateTime? start)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await Service(db).SaveAsync(new AttendanceSettingsInput { StartDate = start }, CancellationToken.None);
    }
}
