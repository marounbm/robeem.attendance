using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Application.Seeding;
using robeem.DataModel;

namespace robeem.Application.HR.Seeding;

/// <summary>
/// Gives every tenant the common work patterns as ready-made time schedules, so an administrator picks one instead of building it:
/// Standard Week, Six-Day Workweek, Weekend Schedule, Sunday-Thursday Workweek, Alternate Weekend Schedule, Four-Day Workweek,
/// Continuous Operations (7-Day) and Custom Workweek. Each has its shifts and its all-inclusive default team, as the schedule form makes them.
/// Adds only the ones a tenant has no schedule of that name for (compared ignoring case), so an administrator's edit is never overwritten;
/// a template that was deleted is made again at the next start.
/// </summary>
public sealed class ScheduleTemplateSeedContributor : ISeedContributor
{
    // A Saturday, on which the first week of the Alternate Weekend Schedule's fortnight starts: cycle day 1 is this date, and every 14 days after it.
    private static readonly DateTime AlternateWeekendAnchor = new(2026, 1, 3, 0, 0, 0, DateTimeKind.Unspecified);

    private static readonly ShiftSpec DayShift = new("Day Shift", "09:00", "17:00", "13:00", "14:00");

    private static readonly ShiftSpec CompressedDay = new("Compressed Day", "07:00", "18:00", "12:00", "13:00");

    private static readonly ShiftSpec MorningShift = new("Morning Shift", "06:00", "14:00", null, null);

    private static readonly ShiftSpec AfternoonShift = new("Afternoon Shift", "14:00", "22:00", null, null);

    private static readonly ShiftSpec NightShift = new("Night Shift", "22:00", "06:00", null, null);

    public int Order => 110;

    public async Task SeedAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        var existing = (await db.TimeSchedules.AsNoTracking().Select(s => s.Title).ToListAsync(cancellationToken))
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();

        foreach (var template in BuildTemplates())
        {
            if (existing.Contains(template.Title.ToLowerInvariant()))
            {
                continue;
            }

            Add(db, template);
        }
    }

    private static IReadOnlyList<Template> BuildTemplates() =>
    [
        new(
            "Standard Week",
            "Monday to Friday, 09:00-17:00 with a one-hour break. Office and corporate.",
            DayOfWeek.Monday,
            7,
            null,
            [DayShift],
            [new("All Employees", true, Days(DayShift, 1, 2, 3, 4, 5))]),

        new(
            "Six-Day Workweek",
            "Monday to Saturday, 09:00-17:00 with a one-hour break. Retail, manufacturing and operations.",
            DayOfWeek.Monday,
            7,
            null,
            [DayShift],
            [new("All Employees", true, Days(DayShift, 1, 2, 3, 4, 5, 6))]),

        new(
            "Weekend Schedule",
            "Saturday and Sunday, 09:00-17:00 with a one-hour break. Retail, hospitality and services.",
            DayOfWeek.Saturday,
            7,
            null,
            [DayShift],
            [new("All Employees", true, Days(DayShift, 1, 2))]),

        new(
            "Sunday-Thursday Workweek",
            "Sunday to Thursday, 09:00-17:00 with a one-hour break. Middle East organizations.",
            DayOfWeek.Sunday,
            7,
            null,
            [DayShift],
            [new("All Employees", true, Days(DayShift, 1, 2, 3, 4, 5))]),

        // A fortnight, from a fixed Saturday: the default team works the first weekend (cycle days 1-2), the other team the second (days 8-9).
        new(
            "Alternate Weekend Schedule",
            "Every other Saturday and Sunday, 09:00-17:00 with a one-hour break: the all-employees team works the first weekend of the fortnight, the Alternate Weekend team the next. Rotational teams.",
            DayOfWeek.Saturday,
            14,
            AlternateWeekendAnchor,
            [DayShift],
            [new("All Employees", true, Days(DayShift, 1, 2)), new("Alternate Weekend", false, Days(DayShift, 8, 9))]),

        new(
            "Four-Day Workweek",
            "Monday to Thursday, 07:00-18:00 with a one-hour break: ten hours a day, a compressed workweek.",
            DayOfWeek.Monday,
            7,
            null,
            [CompressedDay],
            [new("All Employees", true, Days(CompressedDay, 1, 2, 3, 4))]),

        // Three crews cover the clock: each works every day of the week, on a different shift.
        new(
            "Continuous Operations (7-Day)",
            "Every day of the week, around the clock: the all-employees team works the morning shift, the Afternoon and Night teams the others. Move employees between teams to cover the clock.",
            DayOfWeek.Monday,
            7,
            null,
            [MorningShift, AfternoonShift, NightShift],
            [
                new("All Employees", true, Days(MorningShift, 1, 2, 3, 4, 5, 6, 7)),
                new("Afternoon Team", false, Days(AfternoonShift, 1, 2, 3, 4, 5, 6, 7)),
                new("Night Team", false, Days(NightShift, 1, 2, 3, 4, 5, 6, 7)),
            ]),

        // No day is scheduled: the administrator chooses the days and the shift for the business.
        new(
            "Custom Workweek",
            "A starting point with a 09:00-17:00 shift and no working days. Choose the days that fit the business.",
            DayOfWeek.Monday,
            7,
            null,
            [DayShift],
            [new("All Employees", true, [])]),
    ];

    private static IReadOnlyList<(int CycleDay, ShiftSpec Shift)> Days(ShiftSpec shift, params int[] cycleDays) =>
        cycleDays.Select(day => (day, shift)).ToList();

    private static void Add(IRobeemDbContext db, Template template)
    {
        var schedule = new TimeSchedule
        {
            Title = template.Title,
            Description = template.Description,
            CyclePeriod = template.CyclePeriod,
            FirstCycleDay = template.FirstCycleDay,
        };
        if (template.Anchor is { } anchor)
        {
            // A cycle longer than a week needs a fixed starting date: it is cycle day 1.
            (schedule.LastScheduleDate, schedule.LastScheduleDay) = (anchor, 1);
        }

        db.TimeSchedules.Add(schedule);

        var shifts = template.Shifts.ToDictionary(
            spec => spec.Title,
            spec => new TimeScheduleShift
            {
                Title = spec.Title,
                TimeSchedule = schedule,
                StartTime = spec.Start,
                EndTime = spec.End,
                BreakTime = spec.BreakStart is not null,
                BreakStartTime = spec.BreakStart ?? "00:00",
                BreakEndTime = spec.BreakEnd ?? "00:00",
            });
        db.TimeScheduleShifts.AddRange(shifts.Values);

        foreach (var spec in template.Teams)
        {
            var team = new TimeScheduleTeam { Title = spec.Title, TimeSchedule = schedule, AllInclusive = spec.AllInclusive };
            db.TimeScheduleTeams.Add(team);
            foreach (var (cycleDay, shift) in spec.Lines)
            {
                db.TimeScheduleLines.Add(new TimeScheduleLine
                {
                    TimeSchedule = schedule,
                    Team = team,
                    Shift = shifts[shift.Title],
                    CycleDay = cycleDay,
                });
            }
        }
    }

    private sealed record ShiftSpec(string Title, string Start, string End, string? BreakStart, string? BreakEnd);

    private sealed record TeamSpec(string Title, bool AllInclusive, IReadOnlyList<(int CycleDay, ShiftSpec Shift)> Lines);

    private sealed record Template(string Title, string Description, DayOfWeek FirstCycleDay, int CyclePeriod, DateTime? Anchor, IReadOnlyList<ShiftSpec> Shifts, IReadOnlyList<TeamSpec> Teams);
}
