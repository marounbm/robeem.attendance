using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class ScheduleResolutionService : IScheduleResolutionService
{
    private readonly IRobeemDbContext _db;

    public ScheduleResolutionService(IRobeemDbContext db)
    {
        _db = db;
    }

    public async Task<ShiftResolution> ResolveShiftAsync(int employeeId, DateTime date, CancellationToken cancellationToken) =>
        (await ResolveShiftsInRangeAsync(employeeId, date, date, cancellationToken))[0];

    public async Task<IReadOnlyList<ShiftResolution>> ResolveShiftsInRangeAsync(int employeeId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        var from = DateTime.SpecifyKind(fromDate.Date, DateTimeKind.Unspecified);
        var to = DateTime.SpecifyKind(toDate.Date, DateTimeKind.Unspecified);
        if (to < from)
        {
            throw new ArgumentException("The range ends before it starts.", nameof(toDate));
        }

        var dates = Enumerable.Range(0, (to - from).Days + 1).Select(i => from.AddDays(i)).ToList();

        // 1. The employee's assignment. None: not on any schedule; never guessed (CLAUDE.md §3.2).
        var assignment = await _db.TimeScheduleEmployees
            .AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .Select(a => new { a.TimeScheduleId, a.TeamId })
            .SingleOrDefaultAsync(cancellationToken);
        if (assignment is null)
        {
            return dates.Select(ShiftResolution.NoScheduleConfigured).ToList();
        }

        var schedule = await _db.TimeSchedules.AsNoTracking().SingleAsync(s => s.Id == assignment.TimeScheduleId, cancellationToken);

        // 3. The lines that apply to the employee (CLAUDE.md §3.2): those of their own team, and
        // those of the schedule's all-inclusive team, which contains everyone on the schedule.
        // Today both are the same team, as assignment always uses the all-inclusive team.
        var allInclusiveTeamIds = _db.TimeScheduleTeams
            .Where(t => t.TimeScheduleId == assignment.TimeScheduleId && t.AllInclusive)
            .Select(t => t.Id);
        var lines = await _db.TimeScheduleLines
            .AsNoTracking()
            .Where(l => l.TimeScheduleId == assignment.TimeScheduleId
                && (l.TeamId == assignment.TeamId || allInclusiveTeamIds.Contains(l.TeamId)))
            .Select(l => new { l.CycleDay, l.ShiftId })
            .ToListAsync(cancellationToken);

        // 4. Their shifts.
        var shiftIds = lines.Select(l => l.ShiftId).Distinct().ToList();
        var shifts = await _db.TimeScheduleShifts
            .AsNoTracking()
            .Where(s => shiftIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var shiftIdsByCycleDay = lines
            .GroupBy(l => l.CycleDay)
            .ToDictionary(g => g.Key, g => g.Select(l => l.ShiftId).Distinct().ToList());

        return dates.Select(date =>
        {
            // 2. The cycle day. No line for it: a scheduled day off.
            var cycleDay = schedule.GetCycleDay(date);
            if (!shiftIdsByCycleDay.TryGetValue(cycleDay, out var dayShiftIds))
            {
                return ShiftResolution.DayOff(date, cycleDay);
            }

            var resolved = dayShiftIds
                .Select(id => ToResolvedShift(shifts[id], date))
                .OrderBy(s => s.StartsAt)
                .ToList();
            return ShiftResolution.Scheduled(date, cycleDay, resolved);
        }).ToList();
    }

    private static ResolvedShift ToResolvedShift(TimeScheduleShift shift, DateTime date)
    {
        var startsAt = date.Add(Set.ParseTime(shift.StartTime));

        // 5. Overnight is derived; Set.TimeSpan already counts an overnight shift into the next day.
        return new ResolvedShift(
            shift.Id,
            shift.StartTime,
            shift.EndTime,
            shift.BreakTime,
            shift.BreakStartTime,
            shift.BreakEndTime,
            shift.Overtime,
            shift.IsOvernight())
        {
            StartsAt = startsAt,
            EndsAt = startsAt.AddHours(Set.TimeSpan(shift.StartTime, shift.EndTime)),
        };
    }
}
