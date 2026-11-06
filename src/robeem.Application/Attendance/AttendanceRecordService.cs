using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceRecordService : IAttendanceRecordService
{
    private const int MaxPageSize = 200;

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceProcessingEngine _engine;
    private readonly IAttendanceAuditTrail _audit;

    public AttendanceRecordService(IRobeemDbContext db, IAttendanceProcessingEngine engine, IAttendanceAuditTrail audit)
    {
        _db = db;
        _engine = engine;
        _audit = audit;
    }

    public async Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(ExceptionViewer viewer, CancellationToken cancellationToken)
    {
        var employees = _db.Profiles.AsNoTracking();
        if (!viewer.IsHr)
        {
            var me = viewer.EmployeeId;
            var own = viewer.IsEmployee;
            var reports = viewer.IsManager;
            employees = employees.Where(p => (own && p.Id == me) || (reports && p.ManagerId == me));
        }

        var rows = await employees
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ThenBy(p => p.EmployeeNumber)
            .Select(p => new { p.Id, p.FirstName, p.LastName, p.EmployeeNumber })
            .ToListAsync(cancellationToken);
        return rows.Select(p => new EmployeeOption { Id = p.Id, Name = $"{p.FirstName} {p.LastName} ({p.EmployeeNumber})" }).ToList();
    }

    public async Task<PagedResult<RecordDayListItem>> ListDaysAsync(RecordFilter filter, ExceptionViewer viewer, CancellationToken cancellationToken)
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

        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);
        var total = await days.CountAsync(cancellationToken);

        // A page past the end (a filter that narrowed the list since the link was made) is answered with the last page.
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));

        var rows = await Sorted(days, filter.Sort, filter.Descending)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
                HasPendingApproval = _db.AttendanceApprovals.Any(a =>
                    a.Type == AttendanceApprovalType.Correction
                    && a.Status == AttendanceApprovalStatus.Pending
                    && a.EmployeeId == d.EmployeeId
                    && a.AttendanceDate == d.AttendanceDate),
                Status = d.Status.ToString(),
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<RecordDayListItem> { Items = rows, Total = total, Page = page, PageSize = pageSize };
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

        // A review is decided in one step, by the manager or by HR: the step that was taken is the decision.
        var reviews = (await _db.AttendanceApprovals.AsNoTracking()
            .Where(a => a.Type == AttendanceApprovalType.Review
                && a.EmployeeId == employeeId
                && a.AttendanceDate == day
                && (a.Status == AttendanceApprovalStatus.Approved || a.Status == AttendanceApprovalStatus.Rejected))
            .Select(a => new
            {
                a.Id,
                a.AttendanceRecordId,
                a.Status,
                ByHr = a.HrDecision != null,
                DecidedAt = a.HrDecision != null ? a.HrDecidedAt : a.ManagerDecidedAt,
                a.Reason,
                a.OvertimeMinutes,
                By = a.HrDecision != null
                    ? a.HrDecidedBy!.FirstName + " " + a.HrDecidedBy.LastName
                    : a.ManagerDecidedBy!.FirstName + " " + a.ManagerDecidedBy.LastName,
            })
            .ToListAsync(cancellationToken))
            .OrderBy(d => d.DecidedAt).ThenBy(d => d.Id)
            .ToList();

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
                Approval = c.AttendanceApproval!,
                RequestedBy = c.RequestedByEmployee!.FirstName + " " + c.RequestedByEmployee.LastName,
                ManagerBy = c.AttendanceApproval!.ManagerDecidedBy == null ? null : c.AttendanceApproval.ManagerDecidedBy.FirstName + " " + c.AttendanceApproval.ManagerDecidedBy.LastName,
                HrBy = c.AttendanceApproval.HrDecidedBy == null ? null : c.AttendanceApproval.HrDecidedBy.FirstName + " " + c.AttendanceApproval.HrDecidedBy.LastName,
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
                        Outcome = d.Status.ToString(),
                        DecidedByName = d.By,
                        DecidedByRole = d.ByHr ? "HR" : "Manager",
                        DecidedAt = d.DecidedAt ?? default,
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
                    ResolutionNote = x.e.ResolutionNote,
                    RelatedPunchType = x.e.RelatedEventType?.ToString(),
                    RelatedPunchAt = x.e.RelatedEventAt,
                    SuggestedEventTypes = AttendanceExceptionRules.Suggested(x.e.SuggestedEventTypes),
                    CanPropose = AttendanceExceptionRules.CanPropose(x.e.ExceptionType, x.e.Status, x.e.EmployeeId, viewer.EmployeeId, x.e.SuggestedEventTypes),
                    CanCorrect = AttendanceExceptionRules.CanCorrect(x.e.Status, x.e.EmployeeId, viewer.EmployeeId),
                    CanAccept = AttendanceExceptionRules.CanAccept(x.e.ExceptionType, x.e.Status, x.e.EmployeeId, employee.ManagerId, viewer),
                })
                .ToList(),
            Corrections = corrections
                .Select(x => new RecordCorrectionItem
                {
                    Id = x.Correction.Id,
                    Kind = x.Correction.Kind.ToString(),
                    OriginalEventType = x.Correction.OriginalEventType?.ToString(),
                    CorrectedEventType = x.Correction.CorrectedEventType.ToString(),
                    ProposedValue = x.Correction.ProposedValue,
                    Justification = x.Correction.Justification,
                    RequestedByName = x.RequestedBy,
                    RequestedAt = x.Correction.RequestedAt,
                    ApprovalWorkflow = x.Approval.Workflow.ToString(),
                    Status = x.Approval.Status.ToString(),
                    ManagerDecision = x.Approval.ManagerDecision?.ToString(),
                    ManagerDecidedByName = x.ManagerBy,
                    HrDecision = x.Approval.HrDecision?.ToString(),
                    HrDecidedByName = x.HrBy,
                    AppliedAt = x.Approval.AppliedAt,
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

    /// <summary>
    /// The days in the order asked for. Whatever the column, ties are broken by date, then employee name, then id, always the same way round,
    /// so a page never overlaps the next and the order is the same on every request.
    /// </summary>
    private static IQueryable<AttendanceDaySummary> Sorted(IQueryable<AttendanceDaySummary> days, RecordSortField sort, bool descending)
    {
        IOrderedQueryable<AttendanceDaySummary> By<TKey>(Expression<Func<AttendanceDaySummary, TKey>> key) =>
            descending ? days.OrderByDescending(key) : days.OrderBy(key);

        var ordered = sort switch
        {
            RecordSortField.Employee => By(d => d.Employee!.LastName).ThenBy(d => d.Employee!.FirstName),
            RecordSortField.Worked => By(d => d.TotalWorkingHours),
            RecordSortField.Overtime => By(d => d.TotalOvertimeHours),
            RecordSortField.Break => By(d => d.TotalBreakHours),
            RecordSortField.Exceptions => By(d => d.ExceptionCount),
            RecordSortField.Status => By(d => d.Status),
            _ => By(d => d.AttendanceDate),
        };

        if (sort != RecordSortField.Date)
        {
            ordered = ordered.ThenBy(d => d.AttendanceDate);
        }

        if (sort != RecordSortField.Employee)
        {
            ordered = ordered.ThenBy(d => d.Employee!.LastName).ThenBy(d => d.Employee!.FirstName);
        }

        return ordered.ThenBy(d => d.EmployeeId);
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
        BreakFindings = Enum.GetValues<BreakFindings>().Where(f => f != BreakFindings.None && r.BreakFindings.HasFlag(f)).Select(f => f.ToString()).ToList(),
        Status = r.Status.ToString(),
        Reviews = reviews,
    };
}
