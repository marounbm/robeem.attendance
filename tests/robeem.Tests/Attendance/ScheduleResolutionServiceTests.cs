using Microsoft.EntityFrameworkCore;
using robeem.Application.Attendance;
using robeem.Application.HR.Seeding;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

public sealed class ScheduleResolutionServiceTests : IDisposable
{
    // 2026-10-05 is a Monday; Standard Week's cycle starts on Monday.
    private static readonly DateTime Monday = new(2026, 10, 5);

    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task StandardWeek_ResolvesAFullCycle_MondayToFridayWorked_WeekendOff()
    {
        var employeeId = await AssignToStandardWeekAsync();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var days = await new ScheduleResolutionService(db).ResolveShiftsInRangeAsync(employeeId, Monday, Monday.AddDays(6), CancellationToken.None);

        Assert.Equal(7, days.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], days.Select(d => d.CycleDay));
        Assert.Equal(Enumerable.Range(0, 7).Select(i => Monday.AddDays(i)), days.Select(d => d.Date));

        foreach (var weekday in days.Take(5))
        {
            Assert.Equal(ShiftResolutionOutcome.Scheduled, weekday.Outcome);
            var shift = Assert.Single(weekday.Shifts);
            Assert.Equal(("09:00", "17:00"), (shift.StartTime, shift.EndTime));
            Assert.True(shift.BreakTime);
            Assert.Equal(("13:00", "14:00"), (shift.BreakStartTime, shift.BreakEndTime));
            Assert.False(shift.Overtime);
            Assert.False(shift.IsOvernight);
            Assert.Equal(weekday.Date.AddHours(9), shift.StartsAt);
            Assert.Equal(weekday.Date.AddHours(17), shift.EndsAt);
        }

        Assert.All(days.Skip(5), d => Assert.Equal(ShiftResolutionOutcome.DayOff, d.Outcome));
    }

    [Fact]
    public async Task SingleDay_MatchesTheRangeResult()
    {
        var employeeId = await AssignToStandardWeekAsync();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = new ScheduleResolutionService(db);
        var wednesday = await service.ResolveShiftAsync(employeeId, Monday.AddDays(2).AddHours(15), CancellationToken.None);

        Assert.Equal(Monday.AddDays(2), wednesday.Date);
        Assert.Equal(3, wednesday.CycleDay);
        Assert.Equal("09:00", Assert.Single(wednesday.Shifts).StartTime);
    }

    [Fact]
    public async Task EmployeeWithoutAssignment_HasNoScheduleConfigured()
    {
        var employeeId = await AddEmployeeAsync();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var result = await new ScheduleResolutionService(db).ResolveShiftAsync(employeeId, Monday, CancellationToken.None);

        Assert.Equal(ShiftResolutionOutcome.NoScheduleConfigured, result.Outcome);
        Assert.Null(result.CycleDay);
        Assert.Empty(result.Shifts);
    }

    [Fact]
    public async Task CycleDayWithoutLine_IsADayOff()
    {
        var employeeId = await AssignToStandardWeekAsync();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var sunday = await new ScheduleResolutionService(db).ResolveShiftAsync(employeeId, Monday.AddDays(6), CancellationToken.None);

        Assert.Equal(ShiftResolutionOutcome.DayOff, sunday.Outcome);
        Assert.Equal(7, sunday.CycleDay);
        Assert.Empty(sunday.Shifts);
    }

    [Fact]
    public async Task OvernightShift_IsOvernight_AndEndsTheNextDay()
    {
        var employeeId = await AddEmployeeAsync();
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var schedule = new TimeSchedule { Title = "Nights", CyclePeriod = 7, FirstCycleDay = DayOfWeek.Monday };
            var night = new TimeScheduleShift { Title = "Night", TimeSchedule = schedule, StartTime = "22:00", EndTime = "06:00" };
            var team = new TimeScheduleTeam { Title = "All", TimeSchedule = schedule, AllInclusive = true };
            db.AddRange(schedule, night, team, new TimeScheduleLine { TimeSchedule = schedule, Team = team, Shift = night, CycleDay = 1 });
            db.SaveChanges();
            db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { TimeScheduleId = schedule.Id, TeamId = team.Id, EmployeeId = employeeId });
            db.SaveChanges();
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var shift = Assert.Single((await new ScheduleResolutionService(check).ResolveShiftAsync(employeeId, Monday, CancellationToken.None)).Shifts);

        Assert.True(shift.IsOvernight);
        Assert.Equal(Monday.AddHours(22), shift.StartsAt);
        Assert.Equal(Monday.AddDays(1).AddHours(6), shift.EndsAt);
        Assert.Equal(8, Set.TimeSpan(shift.StartTime, shift.EndTime));
    }

    [Fact]
    public async Task EmployeeInASpecificTeam_GetsTheirTeamsLines_AndTheAllInclusiveTeamsLines()
    {
        var employeeId = await AddEmployeeAsync();
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var schedule = new TimeSchedule { Title = "Split", CyclePeriod = 7, FirstCycleDay = DayOfWeek.Monday };
            var morning = new TimeScheduleShift { Title = "Morning", TimeSchedule = schedule, StartTime = "07:00", EndTime = "11:00" };
            var evening = new TimeScheduleShift { Title = "Evening", TimeSchedule = schedule, StartTime = "17:00", EndTime = "21:00" };
            var other = new TimeScheduleShift { Title = "Other", TimeSchedule = schedule, StartTime = "12:00", EndTime = "16:00" };
            var everyone = new TimeScheduleTeam { Title = "All", TimeSchedule = schedule, AllInclusive = true };
            var mine = new TimeScheduleTeam { Title = "Evening crew", TimeSchedule = schedule, AllInclusive = false };
            var theirs = new TimeScheduleTeam { Title = "Day crew", TimeSchedule = schedule, AllInclusive = false };
            db.AddRange(
                schedule,
                new TimeScheduleLine { TimeSchedule = schedule, Team = mine, Shift = evening, CycleDay = 1 },
                new TimeScheduleLine { TimeSchedule = schedule, Team = everyone, Shift = morning, CycleDay = 1 },
                new TimeScheduleLine { TimeSchedule = schedule, Team = theirs, Shift = other, CycleDay = 1 });
            db.SaveChanges();
            db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { TimeScheduleId = schedule.Id, TeamId = mine.Id, EmployeeId = employeeId });
            db.SaveChanges();
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var day = await new ScheduleResolutionService(check).ResolveShiftAsync(employeeId, Monday, CancellationToken.None);

        Assert.Equal(["07:00", "17:00"], day.Shifts.Select(s => s.StartTime));
    }

    [Fact]
    public async Task Range_EndingBeforeItStarts_Throws()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        await Assert.ThrowsAsync<ArgumentException>(
            () => new ScheduleResolutionService(db).ResolveShiftsInRangeAsync(1, Monday, Monday.AddDays(-1), CancellationToken.None));
    }

    [Theory]
    [InlineData("22:00", "06:00", true)]
    [InlineData("23:30", "00:15", true)]
    [InlineData("09:00", "17:00", false)]
    [InlineData("00:00", "08:00", false)]
    public void IsOvernight_ComparesEndWithStart(string start, string end, bool expected)
    {
        Assert.Equal(expected, new TimeScheduleShift { StartTime = start, EndTime = end }.IsOvernight());
    }

    private async Task<int> AddEmployeeAsync()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var employee = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        db.Profiles.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    /// <summary>Seeds the tenant's "Standard Week" and assigns a new employee to its default team, as EmployeeService does.</summary>
    private async Task<int> AssignToStandardWeekAsync()
    {
        var employeeId = await AddEmployeeAsync();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        await new HrSeedContributor().SeedAsync(db, CancellationToken.None);
        await db.SaveChangesAsync();

        var team = await db.TimeScheduleTeams.SingleAsync(t => t.AllInclusive);
        db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { TimeScheduleId = team.TimeScheduleId, TeamId = team.Id, EmployeeId = employeeId });
        await db.SaveChangesAsync();
        return employeeId;
    }
}
