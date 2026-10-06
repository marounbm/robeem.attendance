using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceRecordService : IAttendanceRecordService
{
    private const int MaxTake = 500;

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceProcessingEngine _engine;
    private readonly IAttendanceAuditTrail _audit;

    public AttendanceRecordService(IRobeemDbContext db, IAttendanceProcessingEngine engine, IAttendanceAuditTrail audit)
    {
        _db = db;
        _engine = engine;
        _audit = audit;
    }

    public async Task<IReadOnlyList<RecordDayListItem>> ListDaysAsync(RecordFilter filter, ExceptionViewer viewer, CancellationToken cancellationToken)
    {
        var days = _db.AttendanceDaySummaries.AsNoTracking();

        // The scope first: HR sees everyone; otherwise the viewer's own and, as a manager, their direct reports'.
        if (!viewer.IsHr)
        {
            var me = viewer.EmployeeId;
            var own = viewer.IsEmployee;
            var reports = viewer.IsManager;
            days = days.Where(d => (own && d.EmployeeId == me) || (reports && d.Employee!.ManagerId == me));
        }

        if (filter.From is { } from)
        {
            var first = from.Date;
            days = days.Where(d => d.AttendanceDate >= first);
        }

        if (filter.To is { } to)
        {
            var last = to.Date;
            days = days.Where(d => d.AttendanceDate <= last);
        }

        if (filter.EmployeeId is { } employeeId)
        {
            days = days.Where(d => d.EmployeeId == employeeId);
        }

        if (filter.Status is { } status)
        {
            days = days.Where(d => d.Status == status);
        }

        if (filter.OnlyWithExceptions)
        {
            days = days.Where(d => d.ExceptionCount > 0);
        }

        var rows = await days
            .OrderByDescending(d => d.AttendanceDate)
            .ThenBy(d => d.Employee!.LastName)
            .ThenBy(d => d.Employee!.FirstName)
            .ThenBy(d => d.EmployeeId)
            .Take(Math.Clamp(filter.Take, 1, MaxTake))
            .Select(d => new RecordDayListItem
            {
                EmployeeId = d.EmployeeId,
                EmployeeNumber = d.Employee!.EmployeeNumber,
                EmployeeName = d.Employee.FirstName + " " + d.Employee.LastName,
                AttendanceDate = d.AttendanceDate,
                TotalWorkingHours = d.TotalWorkingHours,
                TotalOvertimeHours = d.TotalOvertimeHours,
                TotalBreakHours = d.TotalBreakHours,
                ShiftCount = d.ShiftCount,
                ExceptionCount = d.ExceptionCount,
                Status = d.Status.ToString(),
            })
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<RecordDayDetail?> GetDayAsync(int employeeId, DateTime date, ExceptionViewer viewer, CancellationToken cancellationToken)
    {
        var employee = await _db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == employeeId, cancellationToken);
        if (employee is null || !CanSee(viewer, employee))
        {
            return null;
        }

        var day = date.Date;

        // The punches are a direct read of normalized attendance outside the engine: audited, and
        // saved before the read, so the read is on record even if it then fails.
        _audit.Record(AttendanceAuditActions.AttendanceDataRead, nameof(NormalizedAttendanceTransaction), null, $"Punches of employee {employeeId} on {day:yyyy-MM-dd}.");
        await _db.SaveChangesAsync(cancellationToken);

        var summary = await _db.AttendanceDaySummaries.AsNoTracking().SingleOrDefaultAsync(s => s.EmployeeId == employeeId && s.AttendanceDate == day, cancellationToken);

        var records = await _db.AttendanceRecords.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && r.AttendanceDate == day)
            .Select(r => new { Record = r, ShiftTitle = r.TimeScheduleShift == null ? null : r.TimeScheduleShift.Title })
            .ToListAsync(cancellationToken);

        var reviews = await _db.AttendanceReviewDecisions.AsNoTracking()
            .Where(d => d.EmployeeId == employeeId && d.AttendanceDate == day)
            .OrderBy(d => d.DecidedAt).ThenBy(d => d.Id)
            .Select(d => new { d.AttendanceRecordId, d.Outcome, d.DecidedByRole, d.DecidedAt, d.Reason, d.OvertimeMinutes, By = d.DecidedBy!.FirstName + " " + d.DecidedBy.LastName })
            .ToListAsync(cancellationToken);

        var exceptions = await _db.AttendanceExceptions.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId && e.AttendanceDate == day)
            .OrderBy(e => e.Id)
            .Select(e => new { e, ShiftTitle = e.TimeScheduleShift != null ? e.TimeScheduleShift.Title : null })
            .ToListAsync(cancellationToken);

        var corrections = await _db.AttendanceCorrections.AsNoTracking()
            .Where(c => c.EmployeeId == employeeId && c.AttendanceDate == day)
            .OrderBy(c => c.RequestedAt).ThenBy(c => c.Id)
            .Select(c => new
            {
                Correction = c,
                RequestedBy = c.RequestedByEmployee!.FirstName + " " + c.RequestedByEmployee.LastName,
                ManagerBy = c.ManagerDecidedBy == null ? null : c.ManagerDecidedBy.FirstName + " " + c.ManagerDecidedBy.LastName,
                HrBy = c.HrDecidedBy == null ? null : c.HrDecidedBy.FirstName + " " + c.HrDecidedBy.LastName,
            })
            .ToListAsync(cancellationToken);

        // Ordered in memory by the instant: not every database can order by a DateTimeOffset.
        var punches = (await _db.NormalizedAttendanceTransactions.AsNoTracking()
            .Where(n => n.EmployeeId == employeeId && n.AttendanceDate == day)
            .Select(n => new { Punch = n, DeviceName = n.Device == null ? null : n.Device.Title })
            .ToListAsync(cancellationToken))
            .OrderBy(x => x.Punch.EventTimestampUtc).ThenBy(x => x.Punch.Id)
            .ToList();

        return new RecordDayDetail
        {
            EmployeeId = employee.Id,
            EmployeeNumber = employee.EmployeeNumber,
            EmployeeName = $"{employee.FirstName} {employee.LastName}",
            AttendanceDate = day,
            Processed = summary is not null,
            TotalWorkingHours = summary?.TotalWorkingHours ?? TimeSpan.Zero,
            TotalOvertimeHours = summary?.TotalOvertimeHours ?? TimeSpan.Zero,
            TotalBreakHours = summary?.TotalBreakHours ?? TimeSpan.Zero,
            ExceptionCount = summary?.ExceptionCount ?? 0,
            Status = (summary?.Status ?? AttendanceRecordStatus.Calculated).ToString(),
            Versions = records
                .OrderBy(r => r.Record.ScheduledStart ?? r.Record.ActualCheckIn ?? day)
                .ThenBy(r => r.Record.TimeScheduleShiftId)
                .ThenByDescending(r => r.Record.Version)
                .Select(r => Version(r.Record, r.ShiftTitle, reviews.Where(d => d.AttendanceRecordId == r.Record.Id)
                    .Select(d => new RecordReviewItem
                    {
                        Outcome = d.Outcome.ToString(),
                        DecidedByName = d.By,
                        DecidedByRole = d.DecidedByRole.ToString(),
                        DecidedAt = d.DecidedAt,
                        Reason = d.Reason,
                        OvertimeMinutes = d.OvertimeMinutes,
                    })
                    .ToList()))
                .ToList(),
            Exceptions = exceptions
                .Select(x => new ExceptionListItem
                {
                    Id = x.e.Id,
                    EmployeeId = x.e.EmployeeId,
                    EmployeeName = $"{employee.FirstName} {employee.LastName}",
                    AttendanceDate = x.e.AttendanceDate,
                    ShiftTitle = x.ShiftTitle,
                    ExceptionType = x.e.ExceptionType.ToString(),
                    MissingEventType = x.e.MissingEventType?.ToString(),
                    Status = x.e.Status.ToString(),
                    Description = x.e.Description,
                    DetectedAt = x.e.DetectedAt,
                    ResolvedAt = x.e.ResolvedAt,
                    ResolutionMethod = x.e.ResolutionMethod?.ToString(),
                    CanPropose = x.e.EmployeeId == viewer.EmployeeId && x.e.ExceptionType == AttendanceExceptionType.MissingPunch && x.e.Status == AttendanceExceptionStatus.Open,
                })
                .ToList(),
            Corrections = corrections
                .Select(x => new RecordCorrectionItem
                {
                    Id = x.Correction.Id,
                    CorrectedEventType = x.Correction.CorrectedEventType.ToString(),
                    ProposedValue = x.Correction.ProposedValue,
                    Justification = x.Correction.Justification,
                    RequestedByName = x.RequestedBy,
                    RequestedAt = x.Correction.RequestedAt,
                    ApprovalWorkflow = x.Correction.ApprovalWorkflow.ToString(),
                    Status = x.Correction.Status.ToString(),
                    ManagerDecision = x.Correction.ManagerDecision?.ToString(),
                    ManagerDecidedByName = x.ManagerBy,
                    HrDecision = x.Correction.HrDecision?.ToString(),
                    HrDecidedByName = x.HrBy,
                    AppliedAt = x.Correction.AppliedAt,
                })
                .ToList(),
            Punches = punches
                .Select(x => new RecordPunchItem
                {
                    Id = x.Punch.Id,
                    LocalTime = x.Punch.EventTimestampLocal,
                    TimeZoneId = x.Punch.TimeZoneId,
                    EventType = x.Punch.NormalizedEventType.ToString(),
                    OriginalEventType = x.Punch.OriginalEventType,
                    DeviceName = x.DeviceName,
                    Confidence = x.Punch.ConfidenceStatus.ToString(),
                    DuplicateStatus = x.Punch.DuplicateStatus.ToString(),
                })
                .ToList(),
        };
    }

    public async Task<ReprocessResponse?> ReprocessAsync(int employeeId, DateTime date, CancellationToken cancellationToken)
    {
        if (!await _db.Profiles.AnyAsync(p => p.Id == employeeId, cancellationToken))
        {
            return null;
        }

        var day = date.Date;
        var result = await _engine.ProcessEmployeeDayAsync(employeeId, day, cancellationToken);

        _audit.Record(
            AttendanceAuditActions.DayReprocessed,
            nameof(Profile),
            employeeId,
            $"Reprocessed {day:yyyy-MM-dd}: {result.RecordsWritten} written, {result.RecordsUnchanged} unchanged, {result.RecordsRetired} retired, {result.ExceptionsRaised} exception(s) raised, {result.ExceptionsResolved} resolved.");
        await _db.SaveChangesAsync(cancellationToken);

        return new ReprocessResponse
        {
            RecordsWritten = result.RecordsWritten,
            RecordsUnchanged = result.RecordsUnchanged,
            RecordsRetired = result.RecordsRetired,
            ExceptionsRaised = result.ExceptionsRaised,
            ExceptionsResolved = result.ExceptionsResolved,
        };
    }

    private static bool CanSee(ExceptionViewer viewer, Profile employee) =>
        viewer.IsHr
        || (viewer.IsEmployee && employee.Id == viewer.EmployeeId)
        || (viewer.IsManager && employee.ManagerId == viewer.EmployeeId);

    private static RecordVersionItem Version(AttendanceRecord r, string? shiftTitle, IReadOnlyList<RecordReviewItem> reviews) => new()
    {
        Id = r.Id,
        Version = r.Version,
        IsCurrent = r.IsCurrent,
        SupersededByRecordId = r.SupersededByRecordId,
        ShiftTitle = shiftTitle,
        ScheduledStart = r.ScheduledStart,
        ScheduledEnd = r.ScheduledEnd,
        ActualCheckIn = r.ActualCheckIn,
        ActualCheckInSource = r.ActualCheckInSource?.ToString(),
        ActualCheckOut = r.ActualCheckOut,
        ActualCheckOutSource = r.ActualCheckOutSource?.ToString(),
        ActualBreakOut = r.ActualBreakOut,
        ActualBreakOutSource = r.ActualBreakOutSource?.ToString(),
        ActualBreakIn = r.ActualBreakIn,
        ActualBreakInSource = r.ActualBreakInSource?.ToString(),
        WorkingHours = r.WorkingHours,
        BreakDuration = r.BreakDuration,
        LateMinutes = r.LateMinutes,
        EarlyDepartureMinutes = r.EarlyDepartureMinutes,
        OvertimeMinutes = r.OvertimeMinutes,
        Status = r.Status.ToString(),
        Reviews = reviews,
    };
}
