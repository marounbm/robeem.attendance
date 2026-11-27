using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceReprocessingService : IAttendanceReprocessingService
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendanceProcessingEngine _engine;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;
    private readonly ILogger<AttendanceReprocessingService> _logger;

    public AttendanceReprocessingService(
        IRobeemDbContext db,
        IAttendanceProcessingEngine engine,
        IAttendanceAuditTrail audit,
        TimeProvider clock,
        ILogger<AttendanceReprocessingService> logger)
    {
        _db = db;
        _engine = engine;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public Task<ReprocessSinceResult> ReprocessSinceAsync(DateTime from, int? employeeId, int maxDays, CancellationToken cancellationToken) =>
        ReprocessRangeAsync(from, DateTime.MaxValue, employeeId, maxDays, cancellationToken);

    public async Task<ReprocessSinceResult> ReprocessRangeAsync(DateTime from, DateTime to, int? employeeId, int maxDays, CancellationToken cancellationToken)
    {
        // The last day is today at the latest: a day that has not happened has nothing to calculate.
        var localToday = _clock.GetLocalNow().DateTime.Date;
        var last = DateTime.SpecifyKind(to.Date < localToday ? to.Date : localToday, DateTimeKind.Unspecified);
        var first = from.Date;

        // Nothing is expected before the date the punching started.
        if (await AttendanceStartDate.ReadAsync(_db, cancellationToken) is { } startDate && startDate.Date > first)
        {
            first = startDate.Date;
        }

        first = DateTime.SpecifyKind(first, DateTimeKind.Unspecified);
        if (first > last)
        {
            return new ReprocessSinceResult(first, last, 0, 0, 0, 0, 0, 0, 0, 0, 0, null);
        }

        var days = new SortedSet<(DateTime Date, int EmployeeId)>();
        await AddDaysWithAttendanceAsync(days, employeeId, first, last, cancellationToken);

        // A closed pay period is payroll's: its days are never calculated again from here.
        var closed = await _db.PayPeriods.AsNoTracking()
            .Where(p => p.IsClosed && p.StartDate <= last && p.EndDate >= first)
            .Select(p => new { p.StartDate, p.EndDate, p.EmployeeId })
            .ToListAsync(cancellationToken);

        // Closed for the whole company, or for that employee: another employee's closed period does not hold a day back.
        var open = days.Where(d => !closed.Any(p => (p.EmployeeId == null || p.EmployeeId == d.EmployeeId) && p.StartDate.Date <= d.Date && d.Date <= p.EndDate.Date)).ToList();
        var inClosedPeriods = days.Count - open.Count;

        // The oldest dates first. A run that stops at its limit finishes the date it is on, so the next run can start at the date after
        // it: a date with more employees than the limit is still done, rather than started over forever.
        var limit = Math.Max(1, maxDays);
        var batch = open.Count <= limit ? open : open.TakeWhile((d, i) => i < limit || d.Date == open[limit - 1].Date).ToList();
        DateTime? nextFrom = batch.Count < open.Count ? open[batch.Count].Date : null;

        int examined = 0, changed = 0, written = 0, retired = 0, sentToReview = 0, raised = 0, resolved = 0, failed = 0;
        foreach (var (date, employee) in batch)
        {
            try
            {
                var result = await _engine.ProcessEmployeeDayAsync(employee, date, cancellationToken);
                examined++;
                changed += result.Changed ? 1 : 0;
                written += result.RecordsWritten;
                retired += result.RecordsRetired;
                sentToReview += result.RecordsSentToReview;
                raised += result.ExceptionsRaised;
                resolved += result.ExceptionsResolved;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The others go on; the day is tried again the next time this is asked.
                _logger.LogError(ex, "Calculating employee {EmployeeId} again for {Date:yyyy-MM-dd} failed.", employee, date);
                _db.ChangeTracker.Clear();
                failed++;
            }
        }

        var details = $"Calculated again from {first:yyyy-MM-dd} to {last:yyyy-MM-dd} ({(employeeId is null ? "everyone" : $"employee {employeeId}")}): {examined} day(s) looked at, {changed} changed "
            + $"({written} record(s) written, {retired} retired, {sentToReview} sent to review, {raised} exception(s) raised, {resolved} closed), {failed} failed, "
            + $"{inClosedPeriods} left alone in closed pay periods{(nextFrom is { } next ? $", the rest from {next:yyyy-MM-dd}" : string.Empty)}.";
        _audit.Record(AttendanceAuditActions.DayReprocessed, "Period", employeeId, details);
        await _db.SaveChangesAsync(cancellationToken);

        return new ReprocessSinceResult(first, last, examined, changed, written, retired, sentToReview, raised, resolved, failed, inClosedPeriods, nextFrom);
    }

    /// <summary>
    /// Adds the days that already hold something to calculate again: a day summary or current record, and an exception still to settle (or a
    /// no-show once excused, which a removed holiday brings back). A day nobody punched on, with nothing held, is not one of them.
    /// </summary>
    private async Task AddDaysWithAttendanceAsync(SortedSet<(DateTime Date, int EmployeeId)> days, int? employeeId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var summaries = await _db.AttendanceDaySummaries.AsNoTracking()
            .Where(s => s.AttendanceDate >= from && s.AttendanceDate <= to && (employeeId == null || s.EmployeeId == employeeId))
            .Select(s => new { s.EmployeeId, s.AttendanceDate })
            .ToListAsync(cancellationToken);
        var records = await _db.AttendanceRecords.AsNoTracking()
            .Where(r => r.IsCurrent && r.AttendanceDate >= from && r.AttendanceDate <= to && (employeeId == null || r.EmployeeId == employeeId))
            .Select(r => new { r.EmployeeId, r.AttendanceDate })
            .ToListAsync(cancellationToken);
        var exceptions = await _db.AttendanceExceptions.AsNoTracking()
            .Where(e => e.AttendanceDate >= from
                && e.AttendanceDate <= to
                && (employeeId == null || e.EmployeeId == employeeId)
                && (e.Status != AttendanceExceptionStatus.Closed || e.ResolutionMethod == AttendanceExceptionResolutionMethod.ExcusedAbsence))
            .Select(e => new { e.EmployeeId, e.AttendanceDate })
            .ToListAsync(cancellationToken);

        foreach (var d in summaries.Concat(records).Concat(exceptions))
        {
            days.Add((d.AttendanceDate.Date, d.EmployeeId));
        }
    }
}
