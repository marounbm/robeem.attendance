using robeem.Application.HR.Absence;

namespace robeem.Application.Attendance;

/// <summary>Arithmetic on excused periods. Pure.</summary>
internal static class ExcusedTime
{
    /// <summary>The minutes, counted once, that the periods cover between <paramref name="from"/> and <paramref name="to"/>.</summary>
    public static int UnionMinutes(IReadOnlyList<ExcusedPeriod> periods, DateTime from, DateTime to)
    {
        if (to <= from || periods.Count == 0)
        {
            return 0;
        }

        var clipped = periods
            .Select(p => (Start: p.Start > from ? p.Start : from, End: p.End < to ? p.End : to))
            .Where(p => p.End > p.Start)
            .OrderBy(p => p.Start)
            .ToList();

        var total = TimeSpan.Zero;
        var cursor = DateTime.MinValue;
        foreach (var (start, end) in clipped)
        {
            var begin = start > cursor ? start : cursor;
            if (end > begin)
            {
                total += end - begin;
                cursor = end;
            }
        }

        return (int)total.TotalMinutes;
    }
}
