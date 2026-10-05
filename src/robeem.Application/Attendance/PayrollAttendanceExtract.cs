using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class PayrollAttendanceExtract : IPayrollAttendanceExtract
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendancePolicyResolver _policies;

    public PayrollAttendanceExtract(IRobeemDbContext db, IAttendancePolicyResolver policies)
    {
        _db = db;
        _policies = policies;
    }

    public async Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedRecordsAsync(DateTime payPeriodStart, DateTime payPeriodEnd, CancellationToken cancellationToken)
    {
        var from = payPeriodStart.Date;
        var to = payPeriodEnd.Date;
        if (to < from)
        {
            throw new ArgumentException("The pay period ends before it starts.", nameof(payPeriodEnd));
        }

        var summaries = await _db.AttendanceDaySummaries
            .AsNoTracking()
            .Where(s => s.AttendanceDate >= from && s.AttendanceDate <= to && s.Status == AttendanceRecordStatus.Approved)
            .ToListAsync(cancellationToken);
        if (summaries.Count == 0)
        {
            return [];
        }

        var closedPeriods = await _db.PayPeriods
            .AsNoTracking()
            .Where(p => p.IsClosed && p.StartDate <= to && p.EndDate >= from)
            .ToListAsync(cancellationToken);

        // Only the current version of a record counts; a retired one is history.
        var records = (await _db.AttendanceRecords
            .AsNoTracking()
            .Where(r => r.IsCurrent && r.AttendanceDate >= from && r.AttendanceDate <= to)
            .ToListAsync(cancellationToken))
            .ToLookup(r => (r.EmployeeId, Date: r.AttendanceDate.Date));

        var unclosedExceptions = (await _db.AttendanceExceptions
            .AsNoTracking()
            .Where(e => e.Status != AttendanceExceptionStatus.Closed && e.AttendanceDate >= from && e.AttendanceDate <= to)
            .Select(e => new { e.EmployeeId, e.AttendanceDate })
            .ToListAsync(cancellationToken))
            .Select(e => (e.EmployeeId, Date: e.AttendanceDate.Date))
            .ToHashSet();

        var corrected = (await _db.AttendanceCorrections
            .AsNoTracking()
            .Where(c => (c.Status == AttendanceCorrectionStatus.Approved || c.Status == AttendanceCorrectionStatus.Applied)
                && c.AttendanceDate >= from && c.AttendanceDate <= to)
            .Select(c => new { c.EmployeeId, c.AttendanceDate })
            .ToListAsync(cancellationToken))
            .Select(c => (c.EmployeeId, Date: c.AttendanceDate.Date))
            .ToHashSet();

        var needsPreApproval = new Dictionary<int, bool>();
        var days = new List<(AttendanceDaySummary Summary, List<AttendanceRecord> Records)>();
        foreach (var summary in summaries)
        {
            var key = (summary.EmployeeId, Date: summary.AttendanceDate.Date);
            var dayRecords = records[key].OrderBy(r => r.ScheduledStart ?? r.ActualCheckIn).ThenBy(r => r.Id).ToList();

            if (closedPeriods.Any(p => p.StartDate.Date <= key.Date && key.Date <= p.EndDate.Date)
                || unclosedExceptions.Contains(key)
                || dayRecords.Count == 0
                || dayRecords.Any(r => r.Status != AttendanceRecordStatus.Approved))
            {
                continue;
            }

            // A record with overtime under a policy that wants it pre-approved needs its correction.
            if (!corrected.Contains(key) && dayRecords.Any(r => r.OvertimeMinutes > 0))
            {
                if (!needsPreApproval.TryGetValue(summary.EmployeeId, out var required))
                {
                    required = (await _policies.ResolveAsync(summary.EmployeeId, cancellationToken)).RequiresOvertimePreApproval;
                    needsPreApproval[summary.EmployeeId] = required;
                }

                if (required)
                {
                    continue;
                }
            }

            days.Add((summary, dayRecords));
        }

        var employeeIds = days.Select(d => d.Summary.EmployeeId).Distinct().ToList();
        var numbers = await _db.Profiles
            .AsNoTracking()
            .Where(p => employeeIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.EmployeeNumber, cancellationToken);

        return days
            .Select(d => new PayrollAttendanceDay(
                d.Summary.EmployeeId,
                numbers.GetValueOrDefault(d.Summary.EmployeeId, string.Empty),
                d.Summary.AttendanceDate.Date,
                d.Summary.TotalWorkingHours,
                d.Summary.TotalOvertimeHours,
                d.Summary.TotalBreakHours,
                d.Records.Select(r => new PayrollAttendanceShift(
                    r.Id,
                    r.TimeScheduleShiftId,
                    r.ScheduledStart,
                    r.ScheduledEnd,
                    r.ActualCheckIn,
                    r.ActualCheckOut,
                    r.WorkingHours,
                    r.BreakDuration,
                    r.LateMinutes,
                    r.EarlyDepartureMinutes,
                    r.OvertimeMinutes)).ToList()))
            .OrderBy(d => d.AttendanceDate)
            .ThenBy(d => d.EmployeeNumber, StringComparer.Ordinal)
            .ToList();
    }
}
