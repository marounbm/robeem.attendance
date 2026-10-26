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

    public Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedRecordsAsync(DateTime payPeriodStart, DateTime payPeriodEnd, CancellationToken cancellationToken) =>
        GetApprovedRecordsAsync(payPeriodStart, payPeriodEnd, null, cancellationToken);

    public async Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedRecordsAsync(DateTime payPeriodStart, DateTime payPeriodEnd, string? employee, CancellationToken cancellationToken)
    {
        var from = payPeriodStart.Date;
        var to = payPeriodEnd.Date;
        if (to < from)
        {
            throw new ArgumentException("The pay period ends before it starts.", nameof(payPeriodEnd));
        }

        // The employee filter is applied in every query below, so a wide range for one employee never loads the others' days.
        var term = employee?.Trim().ToLowerInvariant();
        List<int>? matching = null;
        if (!string.IsNullOrEmpty(term))
        {
            matching = await _db.Profiles
                .AsNoTracking()
                .Where(p => p.EmployeeNumber.ToLower().Contains(term) || (p.FirstName + " " + p.LastName).ToLower().Contains(term))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
            if (matching.Count == 0)
            {
                return [];
            }
        }

        var summaryQuery = _db.AttendanceDaySummaries
            .AsNoTracking()
            .Where(s => s.AttendanceDate >= from && s.AttendanceDate <= to && s.Status == AttendanceRecordStatus.Approved);
        if (matching is not null)
        {
            summaryQuery = summaryQuery.Where(s => matching.Contains(s.EmployeeId));
        }

        var summaries = await summaryQuery.ToListAsync(cancellationToken);
        if (summaries.Count == 0)
        {
            return [];
        }

        var closedPeriods = await _db.PayPeriods
            .AsNoTracking()
            .Where(p => p.IsClosed && p.StartDate <= to && p.EndDate >= from)
            .ToListAsync(cancellationToken);

        // Only the employees that have an approved day in the range matter below, when the caller narrowed to some.
        var summaryEmployees = matching is null ? null : summaries.Select(s => s.EmployeeId).Distinct().ToList();

        // Only the current version of a record counts; a retired one is history.
        var recordQuery = _db.AttendanceRecords
            .AsNoTracking()
            .Where(r => r.IsCurrent && r.AttendanceDate >= from && r.AttendanceDate <= to);
        var exceptionQuery = _db.AttendanceExceptions
            .AsNoTracking()
            .Where(e => e.Status != AttendanceExceptionStatus.Closed && e.AttendanceDate >= from && e.AttendanceDate <= to);
        var decisionQuery = _db.AttendanceReviewDecisions
            .AsNoTracking()
            .Where(d => d.Outcome == AttendanceReviewOutcome.Approved && d.AttendanceDate >= from && d.AttendanceDate <= to);
        if (summaryEmployees is not null)
        {
            recordQuery = recordQuery.Where(r => summaryEmployees.Contains(r.EmployeeId));
            exceptionQuery = exceptionQuery.Where(e => summaryEmployees.Contains(e.EmployeeId));
            decisionQuery = decisionQuery.Where(d => summaryEmployees.Contains(d.EmployeeId));
        }

        var records = (await recordQuery.ToListAsync(cancellationToken))
            .ToLookup(r => (r.EmployeeId, Date: r.AttendanceDate.Date));

        var unclosedExceptions = (await exceptionQuery
            .Select(e => new { e.EmployeeId, e.AttendanceDate })
            .ToListAsync(cancellationToken))
            .Select(e => (e.EmployeeId, Date: e.AttendanceDate.Date))
            .ToHashSet();

        // The records a reviewer approved. A decision belongs to one record version, so one given to a
        // version a recalculation retired never covers the current one.
        var approvedRecordIds = (await decisionQuery
            .Select(d => d.AttendanceRecordId)
            .ToListAsync(cancellationToken))
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

            // A record with overtime under a policy that wants it pre-approved needs its reviewer's approval.
            if (dayRecords.Any(r => r.OvertimeMinutes > 0 && !approvedRecordIds.Contains(r.Id)))
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
        var employees = await _db.Profiles
            .AsNoTracking()
            .Where(p => employeeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.EmployeeNumber, p.FirstName, p.LastName })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return days
            .Select(d => new PayrollAttendanceDay(
                d.Summary.EmployeeId,
                employees.TryGetValue(d.Summary.EmployeeId, out var employee) ? employee.EmployeeNumber : string.Empty,
                employee is null ? string.Empty : $"{employee.FirstName} {employee.LastName}",
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
