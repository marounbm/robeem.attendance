using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.HR.Seeding;
using robeem.Application.Seeding;
using robeem.DataModel;
using robeem.Infrastructure.Seeding;

namespace robeem.Tests.Infrastructure;

/// <summary>The ready-made time schedules every tenant gets: the common work patterns, each with its shifts and default team.</summary>
public sealed class ScheduleTemplateSeedTests : IDisposable
{
    // 2026-10-05 is a Monday.
    private static readonly DateTime Monday = new(2026, 10, 5);

    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task EveryTenant_GetsTheEightSchedules_Once_EachWithOneDefaultTeam()
    {
        await SeedAsync();
        await SeedAsync();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(
            [
                "Alternate Weekend Schedule",
                "Continuous Operations (7-Day)",
                "Custom Workweek",
                "Four-Day Workweek",
                "Six-Day Workweek",
                "Standard Week",
                "Sunday-Thursday Workweek",
                "Weekend Schedule",
            ],
            db.TimeSchedules.Select(s => s.Title).OrderBy(t => t).ToList());
        foreach (var schedule in db.TimeSchedules.ToList())
        {
            Assert.Single(db.TimeScheduleTeams.Where(t => t.TimeScheduleId == schedule.Id && t.AllInclusive));
            Assert.NotEmpty(db.TimeScheduleShifts.Where(s => s.TimeScheduleId == schedule.Id));
        }
    }

    [Theory]
    [InlineData("Standard Week", "Mon Tue Wed Thu Fri")]
    [InlineData("Six-Day Workweek", "Mon Tue Wed Thu Fri Sat")]
    [InlineData("Weekend Schedule", "Sat Sun")]
    [InlineData("Sunday-Thursday Workweek", "Mon Tue Wed Thu Sun")]
    [InlineData("Four-Day Workweek", "Mon Tue Wed Thu")]
    [InlineData("Custom Workweek", "")]
    public async Task EachWeeklySchedule_WorksItsDays_InEveryWeek(string title, string expectedDays)
    {
        await SeedAsync();

        foreach (var week in new[] { 0, 1, 7, 52 })
        {
            Assert.Equal(expectedDays, string.Join(' ', DaysWorked(title, "All Employees", Monday.AddDays(week * 7), 7)));
        }
    }

    [Fact]
    public async Task TheAlternateWeekendSchedule_WorksEveryOtherWeekend_TheDefaultTeamTheFirstAndTheOtherTeamTheSecond()
    {
        await SeedAsync();

        // 2026-10-10 and 2026-10-24 are the default team's weekends; the 3rd and 17th, the other team's.
        var first = new[] { new DateTime(2026, 10, 10), new DateTime(2026, 10, 11), new DateTime(2026, 10, 24), new DateTime(2026, 10, 25) };
        var second = new[] { new DateTime(2026, 10, 3), new DateTime(2026, 10, 4), new DateTime(2026, 10, 17), new DateTime(2026, 10, 18) };
        var title = "Alternate Weekend Schedule";

        Assert.Equal(first, Worked(title, "All Employees", new DateTime(2026, 10, 1), 28));
        Assert.Equal(second, Worked(title, "Alternate Weekend", new DateTime(2026, 10, 1), 28));

        // The pattern holds before the fixed starting date as well.
        Assert.Equal(new DateTime(2025, 12, 27), Worked(title, "Alternate Weekend", new DateTime(2025, 12, 20), 14).First());
    }

    [Fact]
    public async Task ContinuousOperations_CoversEveryDay_WithThreeShifts_OneForEachTeam()
    {
        await SeedAsync();

        var title = "Continuous Operations (7-Day)";
        Assert.Equal("Mon Tue Wed Thu Fri Sat Sun", string.Join(' ', DaysWorked(title, "All Employees", Monday, 7)));
        Assert.Equal("Mon Tue Wed Thu Fri Sat Sun", string.Join(' ', DaysWorked(title, "Afternoon Team", Monday, 7)));
        Assert.Equal("Mon Tue Wed Thu Fri Sat Sun", string.Join(' ', DaysWorked(title, "Night Team", Monday, 7)));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var shifts = db.TimeScheduleShifts.Where(s => s.TimeSchedule!.Title == title).OrderBy(s => s.StartTime).ToList();
        Assert.Equal(["Morning Shift 06:00-14:00", "Afternoon Shift 14:00-22:00", "Night Shift 22:00-06:00"], shifts.Select(s => $"{s.Title} {s.StartTime}-{s.EndTime}"));

        // The night shift ends the next morning, and is still eight hours.
        Assert.Equal(8d, shifts.Single(s => s.Title == "Night Shift").WorkHours);
    }

    [Fact]
    public async Task ATenantsOwnSchedule_IsNeverOverwritten_AndASeededOneIsNotMadeTwice()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            await new SeedDataRunner(db, new ISeedContributor[] { new HrSeedContributor() }, NullLogger<SeedDataRunner>.Instance).RunAsync(CancellationToken.None);
        }

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var standard = db.TimeSchedules.Single();
            standard.Description = "Our own description.";
            standard.Title = "standard WEEK";
            db.SaveChanges();
        }

        await SeedAsync();

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(8, check.TimeSchedules.Count());
        var kept = Assert.Single(check.TimeSchedules, s => s.Title.ToLower() == "standard week");
        Assert.Equal("Our own description.", kept.Description);
    }

    [Fact]
    public async Task TheSchedulesAreSeededInEachTenant_OnItsOwn()
    {
        await SeedAsync();
        using (var other = _database.CreateContext(TestDatabase.TenantB))
        {
            Assert.Empty(other.TimeSchedules);
        }

        await SeedAsync(TestDatabase.TenantB);

        using var db = _database.CreateContext(TestDatabase.TenantB);
        Assert.Equal(8, db.TimeSchedules.Count());
    }

    private async Task SeedAsync(string tenant = TestDatabase.TenantA)
    {
        using var db = _database.CreateContext(tenant);
        await new SeedDataRunner(db, new ISeedContributor[] { new HrSeedContributor(), new ScheduleTemplateSeedContributor() }, NullLogger<SeedDataRunner>.Instance)
            .RunAsync(CancellationToken.None);
    }

    /// <summary>The days (Mon, Tue, ...) in the range the team has a line on, by the schedule's own cycle.</summary>
    private IEnumerable<string> DaysWorked(string title, string team, DateTime from, int days) =>
        Worked(title, team, from, days).Select(d => d.ToString("ddd", global::System.Globalization.CultureInfo.InvariantCulture));

    private List<DateTime> Worked(string title, string team, DateTime from, int days)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var schedule = db.TimeSchedules.Single(s => s.Title == title);
        var teamId = db.TimeScheduleTeams.Single(t => t.TimeScheduleId == schedule.Id && t.Title == team).Id;
        var cycleDays = db.TimeScheduleLines.Where(l => l.TeamId == teamId).Select(l => l.CycleDay).ToList();
        return Enumerable.Range(0, days).Select(i => from.AddDays(i)).Where(d => cycleDays.Contains(schedule.GetCycleDay(d))).ToList();
    }
}
