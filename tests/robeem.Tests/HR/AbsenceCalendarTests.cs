using robeem.Application.HR.Absence;
using robeem.DataModel;

namespace robeem.Tests.HR;

public sealed class AbsenceCalendarTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly int _ada;
    private readonly int _ben;

    public AbsenceCalendarTests()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var ada = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        var ben = new Profile { EmployeeNumber = "E-2", FirstName = "Ben", LastName = "Franklin", Title = "Ben Franklin" };
        db.AddRange(ada, ben);
        db.SaveChanges();
        (_ada, _ben) = (ada.Id, ben.Id);
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AHoliday_ExcusesEveryone_ALeaveOnlyItsOwner()
    {
        Add(new PublicHoliday { Date = new DateTime(2026, 5, 1), Title = "Labour Day" });
        Add(new EmployeeLeave { EmployeeId = _ada, LeaveType = LeaveType.Sick, StartDate = new DateTime(2026, 5, 4), EndDate = new DateTime(2026, 5, 4) });

        var calendar = await GetAsync([_ada, _ben], new DateTime(2026, 5, 1), new DateTime(2026, 5, 31));

        var ada = calendar.For(_ada);
        Assert.Equal([(ExcusedKind.Holiday, "Labour Day", new DateTime(2026, 5, 1)), (ExcusedKind.Leave, "Sick", new DateTime(2026, 5, 4))], ada.Select(p => (p.Kind, p.Reference, p.Date)));
        Assert.All(ada, p => Assert.True(p.IsWholeDay));
        Assert.Equal([ExcusedKind.Holiday], calendar.For(_ben).Select(p => p.Kind));
        Assert.True(calendar.IsWholeDayExcused(_ada, new DateTime(2026, 5, 4)));
        Assert.False(calendar.IsWholeDayExcused(_ben, new DateTime(2026, 5, 4)));
    }

    [Fact]
    public async Task AWholeDayLeaveRange_IsOneEntryPerDay_ClippedToTheRange()
    {
        Add(new EmployeeLeave { EmployeeId = _ada, LeaveType = LeaveType.Annual, StartDate = new DateTime(2026, 8, 3), EndDate = new DateTime(2026, 8, 7) });

        var calendar = await GetAsync([_ada], new DateTime(2026, 8, 5), new DateTime(2026, 8, 20));

        Assert.Equal([new DateTime(2026, 8, 5), new DateTime(2026, 8, 6), new DateTime(2026, 8, 7)], calendar.For(_ada).Select(p => p.Date));
        Assert.Empty((await GetAsync([_ada], new DateTime(2026, 8, 8), new DateTime(2026, 8, 20))).For(_ada));
    }

    [Fact]
    public async Task APartDayLeave_CarriesItsTimes_AndOneRunningPastMidnightEndsTheNextDay()
    {
        Add(new EmployeeLeave { EmployeeId = _ada, LeaveType = LeaveType.Other, StartDate = new DateTime(2026, 8, 3), EndDate = new DateTime(2026, 8, 3), IsPartDay = true, StartTime = "09:30", EndTime = "12:00" });
        Add(new EmployeeLeave { EmployeeId = _ben, LeaveType = LeaveType.Other, StartDate = new DateTime(2026, 8, 3), EndDate = new DateTime(2026, 8, 3), IsPartDay = true, StartTime = "22:00", EndTime = "02:00" });

        var calendar = await GetAsync([_ada, _ben], new DateTime(2026, 8, 3), new DateTime(2026, 8, 3));

        var morning = Assert.Single(calendar.For(_ada));
        Assert.Equal((false, new DateTime(2026, 8, 3, 9, 30, 0), new DateTime(2026, 8, 3, 12, 0, 0)), (morning.IsWholeDay, morning.Start, morning.End));
        Assert.Equal(150, morning.OverlapMinutes(new DateTime(2026, 8, 3, 8, 0, 0), new DateTime(2026, 8, 3, 17, 0, 0)));

        var night = Assert.Single(calendar.For(_ben));
        Assert.Equal((new DateTime(2026, 8, 3, 22, 0, 0), new DateTime(2026, 8, 4, 2, 0, 0)), (night.Start, night.End));
    }

    [Fact]
    public async Task APartDayLeaveOfTheDayBefore_ThatRunsIntoTheRange_IsSeen()
    {
        Add(new EmployeeLeave { EmployeeId = _ben, LeaveType = LeaveType.Other, StartDate = new DateTime(2026, 8, 2), EndDate = new DateTime(2026, 8, 2), IsPartDay = true, StartTime = "22:00", EndTime = "02:00" });

        var seen = await GetAsync([_ben], new DateTime(2026, 8, 3), new DateTime(2026, 8, 3));
        var missed = await GetAsync([_ben], new DateTime(2026, 8, 4), new DateTime(2026, 8, 4));

        Assert.Single(seen.For(_ben));
        Assert.Empty(missed.For(_ben));
    }

    [Fact]
    public async Task NoEmployees_StillGivesTheHolidays()
    {
        Add(new PublicHoliday { Date = new DateTime(2026, 5, 1), Title = "Labour Day" });

        var calendar = await GetAsync([], new DateTime(2026, 5, 1), new DateTime(2026, 5, 1));

        Assert.Single(calendar.Holidays);
        Assert.Single(calendar.For(12345));
    }

    [Fact]
    public async Task AnotherTenantsHolidaysAndLeaves_AreNeverSeen()
    {
        Add(new PublicHoliday { Date = new DateTime(2026, 5, 1), Title = "Labour Day" });
        Add(new EmployeeLeave { EmployeeId = _ada, LeaveType = LeaveType.Sick, StartDate = new DateTime(2026, 5, 4), EndDate = new DateTime(2026, 5, 4) });

        using var other = _database.CreateContext(TestDatabase.TenantB);
        var calendar = await new HrAbsenceCalendar(other).GetAsync([_ada], new DateTime(2026, 5, 1), new DateTime(2026, 5, 31), CancellationToken.None);

        Assert.Empty(calendar.Holidays);
        Assert.Empty(calendar.For(_ada));
    }

    private void Add(object entity)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        db.Add(entity);
        db.SaveChanges();
    }

    private async Task<AbsenceCalendar> GetAsync(int[] employees, DateTime from, DateTime to)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await new HrAbsenceCalendar(db).GetAsync(employees, from, to, CancellationToken.None);
    }
}
