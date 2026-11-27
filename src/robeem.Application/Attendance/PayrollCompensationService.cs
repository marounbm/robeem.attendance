using System.Globalization;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.HR.Absence;
using robeem.Application.HR.Leaves;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Leaves;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class PayrollCompensationService : IPayrollCompensationService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;
    private const int NoteMaxLength = 500;

    private readonly IRobeemDbContext _db;
    private readonly IPayrollAttendanceExtract _extract;
    private readonly IAttendancePolicyResolver _policies;
    private readonly IEmployeeAbsenceCalendar _absences;
    private readonly ILeaveService _leaves;
    private readonly IPayPeriodService _periods;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;

    public PayrollCompensationService(
        IRobeemDbContext db,
        IPayrollAttendanceExtract extract,
        IAttendancePolicyResolver policies,
        IEmployeeAbsenceCalendar absences,
        ILeaveService leaves,
        IPayPeriodService periods,
        IAttendanceAuditTrail audit,
        TimeProvider clock)
    {
        _db = db;
        _extract = extract;
        _policies = policies;
        _absences = absences;
        _leaves = leaves;
        _periods = periods;
        _audit = audit;
        _clock = clock;
    }

    public async Task<PagedResult<CompensationOverviewItem>> ListAsync(string? search, string? mode, DateTime? date, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);
        var employees = Employees(search);
        var total = await employees.CountAsync(cancellationToken);
        var current = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)size)));

        // Shorter employee numbers first, so 9 comes before 10; the id breaks every tie, so pages never overlap.
        var rows = await employees
            .OrderBy(p => p.EmployeeNumber.Length)
            .ThenBy(p => p.EmployeeNumber)
            .ThenBy(p => p.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .Select(p => new Who(p.Id, p.EmployeeNumber, p.FirstName + " " + p.LastName, p.PayBasis == PayBasis.Hourly))
            .ToListAsync(cancellationToken);

        var reports = await ReportsAsync(rows, mode, date, cancellationToken);
        return new PagedResult<CompensationOverviewItem>
        {
            Items = rows.Select(r => ToOverview(reports[r.Id])).ToList(),
            Total = total,
            Page = current,
            PageSize = size,
        };
    }

    public async Task<IReadOnlyList<CompensationOverviewItem>> ListAllAsync(string? search, string? mode, DateTime? date, CancellationToken cancellationToken)
    {
        var all = new List<CompensationOverviewItem>();
        for (var page = 1; ; page++)
        {
            var result = await ListAsync(search, mode, date, page, MaxPageSize, cancellationToken);
            all.AddRange(result.Items);
            if (all.Count >= result.Total || result.Items.Count == 0)
            {
                return all;
            }
        }
    }

    public async Task<CompensationReport?> GetReportAsync(int employeeId, string? mode, DateTime? date, CancellationToken cancellationToken)
    {
        var who = await _db.Profiles.AsNoTracking()
            .Where(p => p.Id == employeeId)
            .Select(p => new Who(p.Id, p.EmployeeNumber, p.FirstName + " " + p.LastName, p.PayBasis == PayBasis.Hourly))
            .SingleOrDefaultAsync(cancellationToken);
        return who is null ? null : (await ReportsAsync([who], mode, date, cancellationToken))[employeeId];
    }

    public async Task<SaveResult> DecideAsync(CompensationDecisionRequest request, int decidedById, CancellationToken cancellationToken)
    {
        var report = await GetReportAsync(request.EmployeeId, IPayrollCompensationService.ContainingPeriod, request.PeriodStart, cancellationToken);
        if (report is null)
        {
            return SaveResult.Missing();
        }

        if (report.PeriodStart.Date != request.PeriodStart.Date)
        {
            return SaveResult.Invalid(nameof(CompensationDecisionRequest.PeriodStart), $"That is not the first day of the employee's pay period: it starts on {report.PeriodStart:yyyy-MM-dd}.");
        }

        if (report.IsClosed)
        {
            return SaveResult.Invalid(nameof(CompensationDecisionRequest.PeriodStart), "The pay period is closed: its decisions are locked.");
        }

        if (!await _db.Profiles.AnyAsync(p => p.Id == decidedById, cancellationToken))
        {
            return SaveResult.Invalid(nameof(CompensationDecisionRequest.EmployeeId), "The user who decides does not exist.");
        }

        var errors = new Dictionary<string, string>();
        var wanted = new List<Wanted>();
        var chosen = new HashSet<(DateTime, string)>();

        foreach (var (item, index) in request.Lines.Select((l, i) => (l, i)))
        {
            var key = $"Lines[{index}]";
            var line = report.Lines.FirstOrDefault(l => l.Date.Date == item.Date.Date && l.LineKind == item.LineKind && !l.IsCoveredByLeave);
            if (line is null || line.LineKind == CompensationLine.Penalty)
            {
                errors[key] = $"There is no {item.LineKind} line on {item.Date:yyyy-MM-dd} to decide.";
            }
            else if (!Enum.TryParse<CompensationDecisionKind>(item.Decision, out var decision) || !Enum.IsDefined(decision))
            {
                errors[key] = $"'{item.Decision}' is not a decision. Use one of: {string.Join(", ", CompensationDecisionRequest.Decisions)}.";
            }
            else if (!line.AllowedDecisions.Contains(decision.ToString()))
            {
                errors[key] = line.AllowedDecisions.Count == 0
                    ? $"The {line.LineKind} line of {line.Date:yyyy-MM-dd} can't be decided now."
                    : $"{decision} is not allowed for the {line.LineKind} line of {line.Date:yyyy-MM-dd}. Use one of: {string.Join(", ", line.AllowedDecisions)}.";
            }
            else if (!chosen.Add((line.Date.Date, line.LineKind)))
            {
                errors[key] = $"The {line.LineKind} line of {line.Date:yyyy-MM-dd} is decided twice in this request.";
            }
            else if (item.Note is { Length: > NoteMaxLength })
            {
                errors[key] = $"The note can be at most {NoteMaxLength} characters.";
            }
            else
            {
                var leaveType = decision == CompensationDecisionKind.UnpaidLeave ? nameof(LeaveType.Unpaid) : item.LeaveType ?? nameof(LeaveType.Annual);
                if (decision == CompensationDecisionKind.PaidLeave && !CompensationDecisionRequest.PaidLeaveTypes.Contains(leaveType, StringComparer.Ordinal))
                {
                    errors[key] = $"Paid leave can be {string.Join(", ", CompensationDecisionRequest.PaidLeaveTypes)}.";
                }
                else
                {
                    wanted.Add(new Wanted(line, decision, leaveType, string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim(), key));
                }
            }
        }

        // One decision for every under time line that has none yet.
        if (request.UndecidedUndertime is { } bulk)
        {
            if (!Enum.TryParse<CompensationDecisionKind>(bulk, out var bulkDecision) || !CompensationCalculator.UndertimeChoices.Contains(bulkDecision))
            {
                errors[nameof(CompensationDecisionRequest.UndecidedUndertime)] = "Decide the under time lines that have no decision with Ignore or Penalise. Leave adds leaves, so it is decided line by line.";
            }
            else
            {
                foreach (var line in report.Lines.Where(l => l is { LineKind: CompensationLine.Undertime, NeedsDecision: true, IsCoveredByLeave: false }))
                {
                    if (chosen.Add((line.Date.Date, line.LineKind)))
                    {
                        wanted.Add(new Wanted(line, bulkDecision, nameof(LeaveType.Annual), null, nameof(CompensationDecisionRequest.UndecidedUndertime)));
                    }
                }
            }
        }

        if (errors.Count == 0 && wanted.Count == 0)
        {
            errors[nameof(CompensationDecisionRequest.Lines)] = "Choose at least one line to decide.";
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var needsDays = wanted.Any(w => w.Decision is CompensationDecisionKind.PaidLeave or CompensationDecisionKind.UnpaidLeave);
        var days = needsDays
            ? (await _extract.GetApprovedRecordsAsync(report.PeriodStart, report.PeriodEnd, [request.EmployeeId], false, cancellationToken)).ToDictionary(d => d.AttendanceDate.Date)
            : new Dictionary<DateTime, PayrollAttendanceDay>();

        var now = _clock.GetLocalNow().DateTime;
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var item in wanted)
            {
                var minutes = item.Line.Minutes;
                var leaveIds = new List<int>();
                if (item.Decision is CompensationDecisionKind.PaidLeave or CompensationDecisionKind.UnpaidLeave)
                {
                    var windows = CompensationCalculator.MissedWindows(days[item.Line.Date.Date]);
                    foreach (var (start, end) in windows)
                    {
                        var created = await _leaves.CreateAsync(
                            new LeaveInput
                            {
                                EmployeeId = request.EmployeeId,
                                LeaveType = item.LeaveType,
                                StartDate = start.Date,
                                EndDate = start.Date,
                                IsPartDay = true,
                                StartTime = start.ToString("HH:mm", CultureInfo.InvariantCulture),
                                EndTime = end.ToString("HH:mm", CultureInfo.InvariantCulture),
                                Notes = $"Under time of {item.Line.Date:yyyy-MM-dd}, decided in the compensation report.",
                            },
                            cancellationToken);
                        if (!created.Succeeded)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            _db.ChangeTracker.Clear();
                            return SaveResult.Invalid(item.Key, created.Errors.Count > 0 ? string.Join(" ", created.Errors.Values) : "The leave could not be added.");
                        }

                        leaveIds.Add(created.Id!.Value);
                    }

                    // What the leave covers is the time it was placed on, never more than the under time of the day.
                    minutes = Math.Min(item.Line.Minutes, windows.Sum(w => (int)(w.End - w.Start).TotalMinutes));
                }

                _db.PayCompensationDecisions.Add(new PayCompensationDecision
                {
                    EmployeeId = request.EmployeeId,
                    AttendanceDate = DateTime.SpecifyKind(item.Line.Date.Date, DateTimeKind.Unspecified),
                    LineKind = Enum.Parse<CompensationLineKind>(item.Line.LineKind),
                    Decision = item.Decision,
                    Minutes = minutes,
                    LeaveIds = string.Join(",", leaveIds),
                    DecidedById = decidedById,
                    DecidedAt = now,
                    Note = item.Note,
                });
                var details = $"Employee {request.EmployeeId}, {item.Line.Date:yyyy-MM-dd}, {item.Line.LineKind}: {item.Decision} ({minutes} min)"
                    + (leaveIds.Count > 0 ? $", leave {string.Join(", ", leaveIds)} added to the employee's leaves" : string.Empty)
                    + (item.Note is null ? "." : $". {item.Note}");
                _audit.Record(AttendanceAuditActions.CompensationDecided, nameof(PayCompensationDecision), null, details);
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            throw;
        }

        return SaveResult.Success(wanted.Count);
    }

    public async Task<SaveResult> ClosePeriodAsync(CompensationCloseRequest request, CancellationToken cancellationToken)
    {
        var report = await GetReportAsync(request.EmployeeId, IPayrollCompensationService.ContainingPeriod, request.PeriodStart, cancellationToken);
        if (report is null)
        {
            return SaveResult.Missing();
        }

        if (report.PeriodStart.Date != request.PeriodStart.Date)
        {
            return SaveResult.Invalid(nameof(CompensationCloseRequest.PeriodStart), $"That is not the first day of the employee's pay period: it starts on {report.PeriodStart:yyyy-MM-dd}.");
        }

        if (report.IsClosed)
        {
            return SaveResult.Invalid(nameof(CompensationCloseRequest.PeriodStart), "The pay period is already closed.");
        }

        return report.CannotCloseReason is { } reason
            ? SaveResult.Invalid(nameof(CompensationCloseRequest.PeriodStart), reason)
            : await _periods.CloseForEmployeeAsync(request.EmployeeId, report.PeriodStart, report.PeriodEnd, cancellationToken);
    }

    public async Task<SaveResult> ReopenPeriodAsync(CompensationReopenRequest request, CancellationToken cancellationToken)
    {
        if (!await _db.Profiles.AnyAsync(p => p.Id == request.EmployeeId, cancellationToken))
        {
            return SaveResult.Missing();
        }

        // The employee's own closed period that starts on that day (their cycle gave its dates when it was closed).
        var start = request.PeriodStart.Date;
        var period = await _db.PayPeriods.AsNoTracking()
            .Where(p => p.EmployeeId == request.EmployeeId && p.StartDate == start)
            .Select(p => new { p.Id, p.IsClosed })
            .SingleOrDefaultAsync(cancellationToken);
        return period is { IsClosed: true }
            ? await _periods.ReopenAsync(period.Id, request.Reason, cancellationToken)
            : SaveResult.Invalid(nameof(CompensationReopenRequest.PeriodStart), "The pay period is not closed.");
    }

    private static CompensationOverviewItem ToOverview(CompensationReport report) => new()
    {
        EmployeeId = report.EmployeeId,
        EmployeeNumber = report.EmployeeNumber,
        EmployeeName = report.EmployeeName,
        ScheduleName = report.ScheduleName,
        PeriodStart = report.PeriodStart,
        PeriodEnd = report.PeriodEnd,
        PeriodHasEnded = report.PeriodHasEnded,
        IsClosed = report.IsClosed,
        NotReadyDays = report.NotReadyDays.Count,
        CanClose = report.CanClose,
        Summary = report.Summary,
    };

    private static string? CannotCloseReason(bool hasEnded, DateTime end, List<DateTime> notReady)
    {
        if (!hasEnded)
        {
            return $"The period ends on {end:yyyy-MM-dd}: it can be closed once it has ended.";
        }

        if (notReady.Count == 0)
        {
            return null;
        }

        var shown = string.Join(", ", notReady.Take(5).Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        return $"{notReady.Count} day(s) of the period are not ready for payroll ({shown}{(notReady.Count > 5 ? ", ..." : string.Empty)}): settle their exceptions and approvals first.";
    }

    private IQueryable<Profile> Employees(string? search)
    {
        var employees = _db.Profiles.AsNoTracking().Where(p => p.IsActive);
        if (string.IsNullOrWhiteSpace(search))
        {
            return employees;
        }

        // PostgreSQL compares text case-sensitively; lower-case both sides so search is not.
        var term = search.Trim().ToLowerInvariant();
        return employees.Where(p => p.EmployeeNumber.ToLower().Contains(term) || (p.FirstName + " " + p.LastName).ToLower().Contains(term));
    }

    /// <summary>The reports of these employees, each for their own period; employees with the same period are read together.</summary>
    private async Task<Dictionary<int, CompensationReport>> ReportsAsync(IReadOnlyList<Who> employees, string? mode, DateTime? date, CancellationToken cancellationToken)
    {
        var today = _clock.GetLocalNow().DateTime.Date;
        var ids = employees.Select(e => e.Id).ToList();
        var fallback = await _db.PayCycles.AsNoTracking().SingleOrDefaultAsync(c => c.EmployeeId == null, cancellationToken);
        var own = (await _db.PayCycles.AsNoTracking().Where(c => c.EmployeeId != null && ids.Contains(c.EmployeeId.Value)).ToListAsync(cancellationToken))
            .ToDictionary(c => c.EmployeeId!.Value);

        var containing = string.Equals(mode, IPayrollCompensationService.ContainingPeriod, StringComparison.OrdinalIgnoreCase);
        var latest = string.Equals(mode, IPayrollCompensationService.LatestPeriod, StringComparison.OrdinalIgnoreCase);
        var latestDays = latest
            ? (await _db.AttendanceDaySummaries.AsNoTracking()
                    .Where(s => ids.Contains(s.EmployeeId))
                    .GroupBy(s => s.EmployeeId)
                    .Select(g => new { EmployeeId = g.Key, Date = g.Max(s => s.AttendanceDate) })
                    .ToListAsync(cancellationToken))
                .ToDictionary(x => x.EmployeeId, x => x.Date.Date)
            : new Dictionary<int, DateTime>();

        var reports = new Dictionary<int, CompensationReport>();
        var groups = employees
            .Select(e =>
            {
                var cycle = own.GetValueOrDefault(e.Id) ?? fallback;
                var period = containing ? PayCycleCalendar.PeriodContaining(cycle, date ?? today)
                    : latestDays.TryGetValue(e.Id, out var lastDay) ? PayCycleCalendar.PeriodContaining(cycle, lastDay)
                    : PayCycleCalendar.LastFinished(cycle, today);
                return (Employee: e, Cycle: PayCycleCalendar.Describe(cycle), Period: period);
            })
            .GroupBy(x => x.Period);

        foreach (var group in groups)
        {
            var built = await BuildAsync(group.Select(x => (x.Employee, x.Cycle)).ToList(), group.Key.Start, group.Key.End, today, cancellationToken);
            foreach (var (id, report) in built)
            {
                reports[id] = report;
            }
        }

        return reports;
    }

    private async Task<Dictionary<int, CompensationReport>> BuildAsync(IReadOnlyList<(Who Employee, string Cycle)> employees, DateTime start, DateTime end, DateTime today, CancellationToken cancellationToken)
    {
        var ids = employees.Select(e => e.Employee.Id).ToList();
        var next = end.AddDays(1);

        // The days payroll may pay for, through the gate. A closed period is still shown (read-only), or its report would be empty.
        var payable = (await _extract.GetApprovedRecordsAsync(start, end, ids, true, cancellationToken)).ToLookup(d => d.EmployeeId);
        var calendar = await _absences.GetAsync(ids, start, end, cancellationToken);

        var summaries = await _db.AttendanceDaySummaries.AsNoTracking()
            .Where(s => ids.Contains(s.EmployeeId) && s.AttendanceDate >= start && s.AttendanceDate < next && s.Status != AttendanceRecordStatus.Calculated)
            .Select(s => new { s.EmployeeId, s.AttendanceDate })
            .ToListAsync(cancellationToken);
        var unsettled = await _db.AttendanceExceptions.AsNoTracking()
            .Where(e => ids.Contains(e.EmployeeId) && e.Status != AttendanceExceptionStatus.Closed && e.AttendanceDate >= start && e.AttendanceDate < next)
            .Select(e => new { e.EmployeeId, e.AttendanceDate })
            .ToListAsync(cancellationToken);
        var closed = await _db.PayPeriods.AsNoTracking()
            .Where(p => p.IsClosed && p.StartDate <= end && p.EndDate >= start && (p.EmployeeId == null || ids.Contains(p.EmployeeId.Value)))
            .Select(p => new { p.EmployeeId, p.StartDate, p.EndDate })
            .ToListAsync(cancellationToken);

        var decisions = await _db.PayCompensationDecisions.AsNoTracking()
            .Where(d => ids.Contains(d.EmployeeId) && d.AttendanceDate >= start && d.AttendanceDate < next)
            .OrderBy(d => d.Id)
            .ToListAsync(cancellationToken);
        var deciderIds = decisions.Select(d => d.DecidedById).Distinct().ToList();
        var deciders = await _db.Profiles.AsNoTracking()
            .Where(p => deciderIds.Contains(p.Id))
            .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName })
            .ToDictionaryAsync(p => p.Id, p => p.Name.Trim(), cancellationToken);

        // A leave decision counts while at least one of the leaves it added still exists.
        var leaveIds = decisions.SelectMany(d => CompensationCalculator.ParseIds(d.LeaveIds)).Distinct().ToList();
        var existingLeaves = leaveIds.Count == 0
            ? new HashSet<int>()
            : (await _db.EmployeeLeaves.AsNoTracking().Where(l => leaveIds.Contains(l.Id)).Select(l => l.Id).ToListAsync(cancellationToken)).ToHashSet();

        var schedules = (await _db.TimeScheduleEmployees.AsNoTracking()
                .Where(e => ids.Contains(e.EmployeeId))
                .Join(_db.TimeSchedules.AsNoTracking(), e => e.TimeScheduleId, s => s.Id, (e, s) => new { e.EmployeeId, s.Title })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First().Title);

        var reports = new Dictionary<int, CompensationReport>();
        foreach (var (employee, cycle) in employees)
        {
            var policy = await _policies.ResolveAsync(employee.Id, cancellationToken);
            var days = payable[employee.Id].OrderBy(d => d.AttendanceDate).ToList();
            var periods = calendar.For(employee.Id);
            var mine = decisions.Where(d => d.EmployeeId == employee.Id).ToList();

            var lines = days
                .SelectMany(day => CompensationCalculator.LinesOf(employee.Hourly, policy, day, CompensationCalculator.IsHolidayOn(periods, day.AttendanceDate.Date), mine, deciders))
                .ToList();

            // A leave decision whose under time is gone is still a line: it was covered.
            lines.AddRange(mine
                .Where(d => d.Decision is CompensationDecisionKind.PaidLeave or CompensationDecisionKind.UnpaidLeave && CompensationCalculator.ParseIds(d.LeaveIds).Any(existingLeaves.Contains))
                .Select(d => CompensationCalculator.CoveredLine(d, deciders)));

            var isClosed = closed.Any(p => p.EmployeeId == employee.Id);
            var ordered = CompensationCalculator.Arrange(lines, isClosed);

            // The days with attendance data that payroll may not pay for yet; a day inside a period closed for the company is not waiting for anything.
            var payableDates = days.Select(d => d.AttendanceDate.Date).ToHashSet();
            var notReady = summaries.Where(s => s.EmployeeId == employee.Id).Select(s => s.AttendanceDate.Date)
                .Concat(unsettled.Where(e => e.EmployeeId == employee.Id).Select(e => e.AttendanceDate.Date))
                .Distinct()
                .Where(d => !payableDates.Contains(d) && !closed.Any(p => p.EmployeeId == null && p.StartDate.Date <= d && d <= p.EndDate.Date))
                .OrderBy(d => d)
                .ToList();

            var hasEnded = end < today;
            var cannotClose = isClosed ? null : CannotCloseReason(hasEnded, end, notReady);

            reports[employee.Id] = new CompensationReport
            {
                EmployeeId = employee.Id,
                EmployeeNumber = employee.Number,
                EmployeeName = employee.Name.Trim(),
                ScheduleName = schedules.GetValueOrDefault(employee.Id) ?? string.Empty,
                CycleDescription = cycle,
                PeriodStart = start,
                PeriodEnd = end,
                PeriodHasEnded = hasEnded,
                IsClosed = isClosed,
                Lines = ordered,
                Summary = CompensationCalculator.Summarise(days, ordered),
                NotReadyDays = notReady,
                CanClose = !isClosed && cannotClose is null,
                CannotCloseReason = cannotClose,
            };
        }

        return reports;
    }

    private sealed record Who(int Id, string Number, string Name, bool Hourly);

    private sealed record Wanted(CompensationLine Line, CompensationDecisionKind Decision, string LeaveType, string? Note, string Key);
}
