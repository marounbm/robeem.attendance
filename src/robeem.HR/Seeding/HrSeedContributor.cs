using Microsoft.EntityFrameworkCore;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;
using robeem.Infrastructure.Seeding;

namespace robeem.HR.Seeding;

/// <summary>
/// Gives a new tenant database one usable time schedule, so employees can be assigned to a
/// schedule right away: "Standard Week", Monday to Friday 09:00-17:00 with a 13:00-14:00 break,
/// and its all-inclusive default team. Runs only while the tenant has no schedule at all.
/// </summary>
public sealed class HrSeedContributor : ISeedContributor
{
    public int Order => 100;

    public async Task SeedAsync(RobeemDbContext db, CancellationToken cancellationToken)
    {
        if (await db.TimeSchedules.AnyAsync(cancellationToken))
        {
            return;
        }

        var schedule = new TimeSchedule
        {
            Title = "Standard Week",
            Description = "Monday to Friday, 09:00-17:00 with a one-hour break.",
            CyclePeriod = 7,
            FirstCycleDay = DayOfWeek.Monday,
        };

        var dayShift = new TimeScheduleShift
        {
            Title = "Day Shift",
            TimeSchedule = schedule,
            StartTime = "09:00",
            EndTime = "17:00",
            BreakTime = true,
            BreakStartTime = "13:00",
            BreakEndTime = "14:00",
        };

        var allEmployees = new TimeScheduleTeam
        {
            Title = "All Employees",
            TimeSchedule = schedule,
            AllInclusive = true,
        };

        db.AddRange(schedule, dayShift, allEmployees);

        // Cycle days 1-5 are Monday-Friday (FirstCycleDay = Monday); days 6-7 have no line: day off.
        for (var cycleDay = 1; cycleDay <= 5; cycleDay++)
        {
            db.TimeScheduleLines.Add(new TimeScheduleLine
            {
                TimeSchedule = schedule,
                Team = allEmployees,
                Shift = dayShift,
                CycleDay = cycleDay,
            });
        }
    }
}
