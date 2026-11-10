using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using robeem.Application.HR.Absence;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceLookbackService : IAttendanceLookbackService
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendanceProcessingEngine _engine;
    private readonly IScheduleResolutionService _schedules;
    private readonly IEmployeeAbsenceCalendar _absences;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;
    private readonly IRuleSetProvider _ruleSets;
    private readonly ILogger<AttendanceLookbackService> _logger;

    public AttendanceLookbackService(
        IRobeemDbContext db,
        IAttendanceProcessingEngine engine,
        IScheduleResolutionService schedules,
        IEmployeeAbsenceCalendar absences,
        IAttendanceAuditTrail audit,
        TimeProvider clock,
        IRuleSetProvider ruleSets,
        ILogger<AttendanceLookbackService> logger)
    {
        _db = db;
        _engine = engine;
        _schedules = schedules;
        _absences = absences;
        _audit = audit;
        _clock = clock;
        _ruleSets = ruleSets;
        _logger = logger;
    }

    public async Task<LookbackResult> RunAsync(int windowDays, int maxDays, CancellationToken cancellationToken)
    {
        var today = _clock.GetLocalNow().DateTime.Date;
        var to = DateTime.SpecifyKind(today.AddDays(-1), DateTimeKind.Unspecified);
        var from = DateTime.SpecifyKind(today.AddDays(-Math.Max(1, windowDays)), DateTimeKind.Unspecified);

        var (checkedDevices, silent) = await CheckDevicesAsync(from, cancellationToken);

        var employees = await _db.Profiles.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new EmployeeInfo(p.Id, p.HireDate, p.TerminationDate, p.PayBasis))
            .ToListAsync(cancellationToken);
        var calendar = await _absences.GetAsync(employees.Select(e => e.Id).ToList(), from, to, cancellationToken);

        var work = new SortedSet<(DateTime Date, int EmployeeId)>();
        var datesWithoutData = await AddMissingDaysAsync(work, employees, calendar, from, to, cancellationToken);
        await AddIncompleteDaysAsync(work, from, to, cancellationToken);
        await AddAbsenceDaysAsync(work, calendar, null, from, to, cancellationToken);
        await AddRuleChangedDaysAsync(work, from, to, cancellationToken);

        var (calculated, failed) = await ProcessAsync(work.Take(Math.Max(1, maxDays)).ToList(), cancellationToken);
        return new LookbackResult(from, to, checkedDevices, silent, calculated, failed, Math.Max(0, work.Count - Math.Max(1, maxDays)), datesWithoutData);
    }

    public async Task<int> ReexamineAbsencesAsync(int? employeeId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var first = DateTime.SpecifyKind(from.Date, DateTimeKind.Unspecified);
        var last = DateTime.SpecifyKind(to.Date, DateTimeKind.Unspecified);
        var ids = await _db.Profiles.AsNoTracking().Where(p => employeeId == null || p.Id == employeeId).Select(p => p.Id).ToListAsync(cancellationToken);
        var calendar = await _absences.GetAsync(ids, first, last, cancellationToken);

        var work = new SortedSet<(DateTime Date, int EmployeeId)>();
        await AddAbsenceDaysAsync(work, calendar, employeeId, first, last, cancellationToken);
        var (calculated, _) = await ProcessAsync(work.ToList(), cancellationToken);
        return calculated;
    }

    /// <summary>
    /// Counts the punches that reached each device on a pulling or pushing connection since <paramref name="from"/>. A device with none is silent.
    /// A device whose files are uploaded by hand is not looked at: it has nothing to send on its own. The read is an aggregate and is audited.
    /// </summary>
    private async Task<(int Checked, IReadOnlyList<SilentDevice> Silent)> CheckDevicesAsync(DateTime from, CancellationToken cancellationToken)
    {
        var devices = await _db.AttendanceDevices.AsNoTracking()
            .Include(d => d.Connection)
            .Include(d => d.Integration).ThenInclude(i => i!.Connections)
            .Where(d => d.Integration!.Status == AttendanceIntegrationStatus.Active)
            .ToListAsync(cancellationToken);

        var live = devices
            .Select(d => (Device: d, Connection: d.Connection ?? d.Integration!.Connections.FirstOrDefault(c => c.IsDefault)))
            .Where(x => x.Connection is { IsActive: true } && x.Connection.Transport != AttendanceTransport.FileUpload)
            .ToList();
        if (live.Count == 0)
        {
            return (0, []);
        }

        var received = (await _db.RawAttendanceTransactions.AsNoTracking()
                .Where(r => r.DeviceId != null && r.ImportBatch!.StartedAt >= from)
                .GroupBy(r => r.DeviceId)
                .Select(g => new { DeviceId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.DeviceId!.Value, x => x.Count);

        _audit.Record(AttendanceAuditActions.AttendanceDataRead, nameof(RawAttendanceTransaction), null, $"Counted the punches of {live.Count} device(s) since {from:yyyy-MM-dd}, for the look-back.");
        await _db.SaveChangesAsync(cancellationToken);

        var silent = live
            .Where(x => !received.TryGetValue(x.Device.Id, out var count) || count == 0)
            .Select(x => new SilentDevice(x.Device.Id, x.Device.Name, x.Device.Integration!.DisplayName, x.Device.LastSeenAt))
            .ToList();
        return (live.Count, silent);
    }

    /// <summary>
    /// Adds the scheduled days of employees paid by the schedule that have no calculation yet, on dates when somebody punched. Hourly staff
    /// are never compared with a schedule. A date nobody punched on is the integration being down, not everyone being away.
    /// </summary>
    private async Task<IReadOnlyList<DateTime>> AddMissingDaysAsync(
        SortedSet<(DateTime Date, int EmployeeId)> work,
        List<EmployeeInfo> employees,
        AbsenceCalendar calendar,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var dataDates = (await _db.NormalizedAttendanceTransactions.AsNoTracking()
                .Where(n => n.AttendanceDate >= from && n.AttendanceDate <= to)
                .Select(n => n.AttendanceDate)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(d => d.Date)
            .ToHashSet();

        // A day counts as calculated when something was worked on it or an exception was raised for it. A shift that was not due yet
        // when it was last calculated has an empty summary, and is looked at again until it is due (a shift that ends after midnight is not
        // due when the job runs at night); a no-show once raised, accepted or excused is never raised a second time here.
        var calculated = (await _db.AttendanceDaySummaries.AsNoTracking()
                .Where(s => s.AttendanceDate >= from && s.AttendanceDate <= to && (s.TotalWorkingHours > TimeSpan.Zero || s.ExceptionCount > 0))
                .Select(s => new { s.EmployeeId, s.AttendanceDate })
                .ToListAsync(cancellationToken))
            .Select(s => (s.EmployeeId, s.AttendanceDate.Date))
            .ToHashSet();
        calculated.UnionWith((await _db.AttendanceExceptions.AsNoTracking()
                .Where(e => e.ExceptionType == AttendanceExceptionType.NoShow && e.AttendanceDate >= from && e.AttendanceDate <= to)
                .Select(e => new { e.EmployeeId, e.AttendanceDate })
                .ToListAsync(cancellationToken))
            .Select(e => (e.EmployeeId, e.AttendanceDate.Date)));

        var withoutData = new SortedSet<DateTime>();
        foreach (var employee in employees.Where(e => e.PayBasis == PayBasis.Scheduled))
        {
            var days = await _schedules.ResolveShiftsInRangeAsync(employee.Id, from, to, cancellationToken);
            foreach (var day in days.Where(d => d.IsScheduled))
            {
                var date = day.Date.Date;
                if (!employee.IsEmployedOn(date) || calculated.Contains((employee.Id, date)) || calendar.IsWholeDayExcused(employee.Id, date))
                {
                    continue;
                }

                if (dataDates.Contains(date))
                {
                    work.Add((date, employee.Id));
                }
                else
                {
                    withoutData.Add(date);
                }
            }
        }

        return withoutData.ToList();
    }

    /// <summary>Adds the days whose current record has a check-in and no check-out and which have no exception yet: the engine raises the missing check-out once the grace has passed.</summary>
    private async Task AddIncompleteDaysAsync(SortedSet<(DateTime Date, int EmployeeId)> work, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var withException = (await _db.AttendanceExceptions.AsNoTracking()
                .Where(e => e.AttendanceDate >= from && e.AttendanceDate <= to && e.Status != AttendanceExceptionStatus.Closed)
                .Select(e => new { e.EmployeeId, e.AttendanceDate })
                .ToListAsync(cancellationToken))
            .Select(e => (e.EmployeeId, e.AttendanceDate.Date))
            .ToHashSet();

        var open = await _db.AttendanceRecords.AsNoTracking()
            .Where(r => r.IsCurrent && r.AttendanceDate >= from && r.AttendanceDate <= to && r.ActualCheckIn != null && r.ActualCheckOut == null)
            .Select(r => new { r.EmployeeId, r.AttendanceDate })
            .ToListAsync(cancellationToken);
        foreach (var day in open.Where(r => !withException.Contains((r.EmployeeId, r.AttendanceDate.Date))))
        {
            work.Add((day.AttendanceDate.Date, day.EmployeeId));
        }
    }

    /// <summary>
    /// Adds the days a leave or holiday entered, changed or removed since may affect: an open no-show (an excuse may exist now), a no-show
    /// once excused (the excuse may be gone), and records of employees with part-day leave, or whose record counted some, which the
    /// engine compares by <c>ExcusedMinutes</c> and rewrites only when the calculation changed.
    /// </summary>
    private async Task AddAbsenceDaysAsync(
        SortedSet<(DateTime Date, int EmployeeId)> work,
        AbsenceCalendar calendar,
        int? employeeId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var noShows = await _db.AttendanceExceptions.AsNoTracking()
            .Where(e => e.ExceptionType == AttendanceExceptionType.NoShow
                && e.AttendanceDate >= from
                && e.AttendanceDate <= to
                && (employeeId == null || e.EmployeeId == employeeId)
                && (e.Status == AttendanceExceptionStatus.Open
                    || (e.Status == AttendanceExceptionStatus.Closed && e.ResolutionMethod == AttendanceExceptionResolutionMethod.ExcusedAbsence)))
            .Select(e => new { e.EmployeeId, e.AttendanceDate, e.Status })
            .ToListAsync(cancellationToken);
        foreach (var e in noShows)
        {
            var date = e.AttendanceDate.Date;
            var excused = calendar.IsWholeDayExcused(e.EmployeeId, date);

            // Open: it may be excused now. Closed as excused: it may no longer be.
            if ((e.Status == AttendanceExceptionStatus.Open && excused) || (e.Status == AttendanceExceptionStatus.Closed && !excused))
            {
                work.Add((date, e.EmployeeId));
            }
        }

        var records = await _db.AttendanceRecords.AsNoTracking()
            .Where(r => r.IsCurrent
                && r.AttendanceDate >= from
                && r.AttendanceDate <= to
                && (employeeId == null || r.EmployeeId == employeeId)
                && r.ScheduledStart != null
                && r.ScheduledEnd != null)
            .Select(r => new { r.EmployeeId, r.AttendanceDate, r.ScheduledStart, r.ScheduledEnd, r.ExcusedMinutes })
            .ToListAsync(cancellationToken);
        foreach (var r in records)
        {
            var overlaps = calendar.For(r.EmployeeId).Any(p => !p.IsWholeDay && p.Overlaps(r.ScheduledStart!.Value, r.ScheduledEnd!.Value));
            if (overlaps || r.ExcusedMinutes > 0)
            {
                work.Add((r.AttendanceDate.Date, r.EmployeeId));
            }
        }
    }

    /// <summary>
    /// Adds the days last calculated with other rules than those in force now: a rule set was published, or taken away, since. The rules
    /// of an employee are those of their schedule's policy scope, else the tenant-wide ones. A day that comes out the same is not
    /// rewritten; it only notes the version it was checked with, so it is not looked at again.
    /// </summary>
    private async Task AddRuleChangedDaysAsync(SortedSet<(DateTime Date, int EmployeeId)> work, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var published = await _ruleSets.GetPublishedVersionsAsync(cancellationToken);
        var records = await _db.AttendanceRecords.AsNoTracking()
            .Where(r => r.IsCurrent && r.AttendanceDate >= from && r.AttendanceDate <= to && (r.RuleSetVersion != 0 || published.Count > 0))
            .Select(r => new { r.EmployeeId, r.AttendanceDate, r.RuleSetVersion })
            .ToListAsync(cancellationToken);
        if (records.Count == 0)
        {
            return;
        }

        var scopesWithPolicy = (await _db.AttendancePolicies.AsNoTracking().Where(p => p.TimeScheduleId != null).Select(p => p.TimeScheduleId!.Value).ToListAsync(cancellationToken)).ToHashSet();
        var scheduleOf = (await _db.TimeScheduleEmployees.AsNoTracking().Select(a => new { a.EmployeeId, a.TimeScheduleId }).ToListAsync(cancellationToken))
            .ToDictionary(a => a.EmployeeId, a => a.TimeScheduleId);

        foreach (var r in records)
        {
            var scope = scheduleOf.TryGetValue(r.EmployeeId, out var scheduleId) && scopesWithPolicy.Contains(scheduleId) ? scheduleId : IRuleSetProvider.TenantScope;
            if (r.RuleSetVersion != published.GetValueOrDefault(scope))
            {
                work.Add((r.AttendanceDate.Date, r.EmployeeId));
            }
        }
    }

    private async Task<(int Calculated, int Failed)> ProcessAsync(IReadOnlyList<(DateTime Date, int EmployeeId)> days, CancellationToken cancellationToken)
    {
        int calculated = 0, failed = 0;
        foreach (var (date, employeeId) in days)
        {
            try
            {
                await _engine.ProcessEmployeeDayAsync(employeeId, date, cancellationToken);
                calculated++;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The day is tried again on the next run; the others go on.
                _logger.LogError(ex, "The look-back could not calculate employee {EmployeeId} for {Date:yyyy-MM-dd}.", employeeId, date);
                _db.ChangeTracker.Clear();
                failed++;
            }
        }

        return (calculated, failed);
    }

    private sealed record EmployeeInfo(int Id, DateTime HireDate, DateTime? TerminationDate, PayBasis PayBasis)
    {
        public bool IsEmployedOn(DateTime date) => date >= HireDate.Date && (TerminationDate is null || date <= TerminationDate.Value.Date);
    }
}
