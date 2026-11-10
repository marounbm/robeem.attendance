using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.HR.Absence;

/// <summary>Reads the excused time from the holiday and leave tables of the current tenant.</summary>
public sealed class HrAbsenceCalendar : IEmployeeAbsenceCalendar
{
    private readonly IRobeemDbContext _db;

    public HrAbsenceCalendar(IRobeemDbContext db)
    {
        _db = db;
    }

    public async Task<AbsenceCalendar> GetAsync(IReadOnlyCollection<int> employeeIds, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var first = from.Date;
        var last = to.Date;
        var end = last.AddDays(1);

        var holidays = (await _db.PublicHolidays
                .AsNoTracking()
                .Where(h => h.Date >= first && h.Date <= last)
                .OrderBy(h => h.Date)
                .Select(h => new { h.Date, h.Title })
                .ToListAsync(cancellationToken))
            .Select(h => new ExcusedPeriod(ExcusedKind.Holiday, h.Title, h.Date.Date, h.Date.Date.AddDays(1), IsWholeDay: true))
            .ToList();

        var leaves = new Dictionary<int, List<ExcusedPeriod>>();
        if (employeeIds.Count == 0)
        {
            return new AbsenceCalendar(holidays, leaves);
        }

        // A part-day leave can run past midnight, so a leave dated the day before the range may still overlap it.
        var rows = await _db.EmployeeLeaves
            .AsNoTracking()
            .Where(l => employeeIds.Contains(l.EmployeeId) && l.EndDate >= first.AddDays(-1) && l.StartDate <= last)
            .OrderBy(l => l.StartDate)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);

        foreach (var leave in rows)
        {
            foreach (var period in Expand(leave).Where(p => p.Overlaps(first, end)))
            {
                if (!leaves.TryGetValue(leave.EmployeeId, out var list))
                {
                    leaves[leave.EmployeeId] = list = [];
                }

                list.Add(period);
            }
        }

        return new AbsenceCalendar(holidays, leaves);
    }

    private static IEnumerable<ExcusedPeriod> Expand(EmployeeLeave leave)
    {
        var reference = leave.LeaveType.ToString();
        if (leave.IsPartDay)
        {
            var start = leave.StartDate.Date.Add(Set.ParseTime(leave.StartTime));
            var end = leave.StartDate.Date.Add(Set.ParseTime(leave.EndTime));
            yield return new ExcusedPeriod(ExcusedKind.Leave, reference, start, end <= start ? end.AddDays(1) : end, IsWholeDay: false);
            yield break;
        }

        for (var day = leave.StartDate.Date; day <= leave.EndDate.Date; day = day.AddDays(1))
        {
            yield return new ExcusedPeriod(ExcusedKind.Leave, reference, day, day.AddDays(1), IsWholeDay: true);
        }
    }
}
