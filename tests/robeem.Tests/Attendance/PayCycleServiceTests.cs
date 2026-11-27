using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>How often each employee is paid: the tenant's default cycle, an employee's own, and the list of who has what.</summary>
public sealed class PayCycleServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly FixedTimeProvider _clock = new(new DateTime(2026, 11, 12, 10, 0, 0));

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task WithNoCycleAtAll_TheCalendarMonthApplies_AndTheDefaultIsEmpty()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var described = await Service(db).DescribeDefaultAsync(CancellationToken.None);

        Assert.Null(await Service(db).GetDefaultAsync(CancellationToken.None));
        Assert.Equal("Calendar month", described.Description);
        Assert.Equal((new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)), (described.CurrentStart, described.CurrentEnd));
        Assert.Equal((new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)), (described.LastStart, described.LastEnd));
    }

    [Fact]
    public async Task TheDefaultIsSetOnce_ThenChanged_AndEachChangeIsAudited()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            Assert.True((await Service(db).SaveDefaultAsync(new PayCycleInput { Frequency = "Monthly", StartDay = 26 }, CancellationToken.None)).Succeeded);
            Assert.True((await Service(db).SaveDefaultAsync(new PayCycleInput { Frequency = "Weekly", AnchorDate = new DateTime(2026, 4, 1, 15, 0, 0) }, CancellationToken.None)).Succeeded);
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var cycle = Assert.Single(check.PayCycles);
        Assert.Equal((PayCycleFrequency.Weekly, new DateTime(2026, 4, 1), (int?)null), (cycle.Frequency, cycle.AnchorDate, cycle.EmployeeId));
        var entries = check.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.PayCycleChanged).OrderBy(a => a.Id).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains("Set the default pay cycle: Monthly, from the 26th", entries[0].Details);
        Assert.Contains("Changed the default pay cycle: Weekly", entries[1].Details);
    }

    [Theory]
    [InlineData("Daily", 1, "Frequency")]
    [InlineData("Monthly", 0, "StartDay")]
    [InlineData("Monthly", 29, "StartDay")]
    public async Task ACycleThatIsNotOneIsRefused_AndNothingIsSaved(string frequency, int startDay, string field)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var result = await Service(db).SaveDefaultAsync(new PayCycleInput { Frequency = frequency, StartDay = startDay }, CancellationToken.None);

        Assert.Contains(field, result.Errors.Keys);
        Assert.Empty(db.PayCycles);
    }

    [Fact]
    public async Task AnEmployeeCanHaveTheirOwnCycle_AndTheListSaysWhichApplies()
    {
        var (ada, grace, alan) = AddEmployees();
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            await Service(db).SaveDefaultAsync(new PayCycleInput { Frequency = "Monthly", StartDay = 26 }, CancellationToken.None);
            Assert.True((await Service(db).SaveEmployeeAsync(grace, new PayCycleInput { Frequency = "Weekly", AnchorDate = new DateTime(2026, 4, 1) }, CancellationToken.None)).Succeeded);
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var page = await Service(check).ListEmployeesAsync(null, 1, 20, CancellationToken.None);

        Assert.Equal([ada, grace, alan], page.Items.Select(i => i.EmployeeId));
        Assert.Equal(["Default", "Own", "Default"], page.Items.Select(i => i.Source));
        Assert.Equal("Monthly, from the 26th", page.Items[0].Cycle.Description);
        Assert.Equal("Weekly, from 2026-04-01 (Wed)", page.Items[1].Cycle.Description);
        Assert.Equal((new DateTime(2026, 11, 11), new DateTime(2026, 11, 17)), (page.Items[1].Cycle.CurrentStart, page.Items[1].Cycle.CurrentEnd));
        Assert.Equal((new DateTime(2026, 10, 26), new DateTime(2026, 11, 25)), (page.Items[0].Cycle.CurrentStart, page.Items[0].Cycle.CurrentEnd));
        Assert.NotNull(await Service(check).GetEmployeeAsync(grace, CancellationToken.None));
        Assert.Null(await Service(check).GetEmployeeAsync(ada, CancellationToken.None));
    }

    [Fact]
    public async Task RemovingAnEmployeesCycle_GivesThemTheDefaultAgain_AndAnUnknownEmployeeIsMissing()
    {
        var (_, grace, _) = AddEmployees();
        using var db = _database.CreateContext(TestDatabase.TenantA);
        await Service(db).SaveEmployeeAsync(grace, new PayCycleInput { Frequency = "EveryTwoWeeks", AnchorDate = new DateTime(2026, 4, 1) }, CancellationToken.None);

        Assert.True((await Service(db).RemoveEmployeeAsync(grace, CancellationToken.None)).Succeeded);

        Assert.Empty(db.PayCycles);
        Assert.True((await Service(db).RemoveEmployeeAsync(grace, CancellationToken.None)).NotFound);
        Assert.True((await Service(db).SaveEmployeeAsync(9999, new PayCycleInput(), CancellationToken.None)).NotFound);
        Assert.Equal(2, db.AttendanceAuditLogs.Count(a => a.Action == AttendanceAuditActions.PayCycleChanged));
    }

    [Fact]
    public async Task TheEmployeeList_IsPagedByNumber_FollowsTheSearch_AndAPageOutOfRangeIsTheLast()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            for (var i = 1; i <= 25; i++)
            {
                db.Profiles.Add(new Profile { EmployeeNumber = $"E-{i}", FirstName = $"First{i}", LastName = "Person", Title = $"First{i} Person" });
            }

            db.Profiles.Add(new Profile { EmployeeNumber = "X-1", FirstName = "Left", LastName = "Company", Title = "Left Company", IsActive = false });
            db.SaveChanges();
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var first = await Service(check).ListEmployeesAsync(null, 1, 10, CancellationToken.None);
        var second = await Service(check).ListEmployeesAsync(null, 2, 10, CancellationToken.None);
        var third = await Service(check).ListEmployeesAsync(null, 3, 10, CancellationToken.None);
        var past = await Service(check).ListEmployeesAsync(null, 99, 10, CancellationToken.None);
        var searched = await Service(check).ListEmployeesAsync("FIRST1", 1, 50, CancellationToken.None);

        // Every active employee once, in order (E-2 before E-10), and an employee who left is not listed.
        var all = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.EmployeeNumber).ToList();
        Assert.Equal(Enumerable.Range(1, 25).Select(i => $"E-{i}").OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal), all);
        Assert.Equal((25, 3), (first.Total, past.Page));
        Assert.Equal(["E-1", "E-10", "E-11", "E-12", "E-13", "E-14", "E-15", "E-16", "E-17", "E-18", "E-19"], searched.Items.Select(i => i.EmployeeNumber).OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal));
        Assert.Equal(5, past.Items.Count);
    }

    private (int Ada, int Grace, int Alan) AddEmployees()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var ada = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        var grace = new Profile { EmployeeNumber = "E-2", FirstName = "Grace", LastName = "Hopper", Title = "Grace Hopper" };
        var alan = new Profile { EmployeeNumber = "E-3", FirstName = "Alan", LastName = "Turing", Title = "Alan Turing" };
        db.AddRange(ada, grace, alan);
        db.SaveChanges();
        return (ada.Id, grace.Id, alan.Id);
    }

    private PayCycleService Service(RobeemDbContext db) =>
        new(db, new AttendanceAuditTrail(db, new TestUser(7), _clock), _clock);
}
