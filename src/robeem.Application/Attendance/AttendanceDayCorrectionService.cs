using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceDayCorrectionService : IAttendanceDayCorrectionService
{
    private const int JustificationMaxLength = 1000;
    private const string EditsKey = "Edits";

    // How long after the end of a shift that runs past midnight its punches are still shown.
    private static readonly TimeSpan LateWindow = TimeSpan.FromHours(4);

    private static readonly NormalizedEventType[] AttendanceTypes =
    [
        NormalizedEventType.CheckIn,
        NormalizedEventType.BreakOut,
        NormalizedEventType.BreakIn,
        NormalizedEventType.CheckOut,
    ];

    private readonly IRobeemDbContext _db;
    private readonly IScheduleResolutionService _schedules;
    private readonly IAttendanceExceptionQuery _exceptions;
    private readonly IAttendancePolicyResolver _policies;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;

    public AttendanceDayCorrectionService(
        IRobeemDbContext db,
        IScheduleResolutionService schedules,
        IAttendanceExceptionQuery exceptions,
        IAttendancePolicyResolver policies,
        IAttendanceAuditTrail audit,
        TimeProvider clock)
    {
        _db = db;
        _schedules = schedules;
        _exceptions = exceptions;
        _policies = policies;
        _audit = audit;
        _clock = clock;
    }

    public async Task<DayCorrectionView?> GetDayAsync(int employeeId, DateTime date, int viewerEmployeeId, CancellationToken cancellationToken) =>
        employeeId == viewerEmployeeId ? (await LoadAsync(employeeId, date.Date, cancellationToken))?.View : null;

    public async Task<SaveResult> ProposeAsync(int employeeId, DayCorrectionRequest request, int requestedByEmployeeId, CancellationToken cancellationToken)
    {
        if (employeeId != requestedByEmployeeId)
        {
            return SaveResult.Invalid(CorrectionErrorKeys.RequestedBy, "A day can only be corrected by the employee it belongs to.");
        }

        var day = request.Date.Date;
        if (await LoadAsync(employeeId, day, cancellationToken) is not { } data)
        {
            return SaveResult.Missing();
        }

        var errors = new Dictionary<string, string>();
        var justification = (request.Justification ?? string.Empty).Trim();
        if (justification.Length == 0)
        {
            errors[nameof(DayCorrectionRequest.Justification)] = "A justification is required.";
        }
        else if (justification.Length > JustificationMaxLength)
        {
            errors[nameof(DayCorrectionRequest.Justification)] = $"The justification can be at most {JustificationMaxLength} characters.";
        }

        if (!data.View.CanCorrect)
        {
            errors["Date"] = data.View.CannotCorrectReason ?? "This day can't be corrected now.";
        }

        var rows = new List<AttendanceCorrection>();
        if (request.Edits.Count == 0)
        {
            errors[EditsKey] = "Choose at least one change: change a punch, add a missing one, or ignore one.";
        }
        else if (errors.Count == 0)
        {
            await BuildRowsAsync(request, data, day, employeeId, rows, errors, cancellationToken);
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var policy = await _policies.ResolveAsync(employeeId, cancellationToken);
        var now = _clock.GetLocalNow().DateTime;
        var requestId = Guid.NewGuid();
        foreach (var row in rows)
        {
            row.RequestId = requestId;
            row.EmployeeId = employeeId;
            row.AttendanceDate = day;
            row.Justification = justification;
            row.RequestedByEmployeeId = requestedByEmployeeId;
            row.RequestedAt = now;
            row.ApprovalWorkflow = policy.CorrectionApprovalWorkflow;
            row.Status = AttendanceCorrectionStatus.Pending;
        }

        _db.AttendanceCorrections.AddRange(rows);

        // The day's open exceptions wait for the decision together.
        var actor = $"employee:{requestedByEmployeeId}";
        var open = await _db.AttendanceExceptions
            .Where(e => e.EmployeeId == employeeId && e.AttendanceDate == day && e.Status == AttendanceExceptionStatus.Open)
            .ToListAsync(cancellationToken);
        foreach (var exception in open)
        {
            exception.Status = AttendanceExceptionStatus.PendingCorrection;
            _audit.Record(AttendanceAuditActions.ExceptionStatusChanged, nameof(AttendanceException), exception.Id, $"Open -> PendingCorrection: a correction of the day ({rows.Count} edit(s)) was proposed.", actor);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(rows[0].Id);
    }

    private static string? ParseEventType(string? name, out NormalizedEventType type)
    {
        type = default;
        return Enum.TryParse(name, ignoreCase: true, out type) && AttendanceTypes.Contains(type)
            ? null
            : $"'{name}' is not a type of punch. Use one of: {string.Join(", ", AttendanceTypes)}.";
    }

    private static string Describe(ResolvedShift shift) =>
        $"{shift.StartsAt:HH:mm}–{shift.EndsAt:HH:mm}" + (shift.BreakTime ? $", break {shift.BreakStartTime}–{shift.BreakEndTime}" : string.Empty);

    private async Task BuildRowsAsync(
        DayCorrectionRequest request,
        DayData data,
        DateTime day,
        int employeeId,
        List<AttendanceCorrection> rows,
        Dictionary<string, string> errors,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var touched = new HashSet<int>();
        var added = new List<(NormalizedEventType Type, DateTime At)>();
        foreach (var (edit, index) in request.Edits.Select((e, i) => (e, i + 1)))
        {
            var label = $"Edit {index}";
            switch (edit.Action)
            {
                case DayCorrectionRequest.Change:
                case DayCorrectionRequest.Ignore:
                    if (edit.TransactionId is not { } id || !data.Punches.TryGetValue(id, out var punch) || !punch.Item.Editable)
                    {
                        problems.Add($"{label}: that punch is not one of this day's that count, so it can't be changed or ignored.");
                    }
                    else if (!touched.Add(id))
                    {
                        problems.Add($"{label}: the punch at {punch.Item.At:HH:mm} is already changed or ignored by another edit.");
                    }
                    else if (edit.Action == DayCorrectionRequest.Ignore)
                    {
                        rows.Add(new AttendanceCorrection
                        {
                            Kind = AttendanceCorrectionKind.IgnorePunch,
                            NormalizedTransactionId = id,
                            OriginalEventType = punch.Type,
                            CorrectedEventType = punch.Type,
                            ProposedValue = punch.Item.At,
                            TimeZoneId = punch.TimeZoneId,
                        });
                    }
                    else if (ParseEventType(edit.EventType, out var newType) is { } typeProblem)
                    {
                        problems.Add($"{label}: {typeProblem}");
                    }
                    else if (newType == punch.Type)
                    {
                        problems.Add($"{label}: the punch at {punch.Item.At:HH:mm} is already a {newType}.");
                    }
                    else
                    {
                        rows.Add(new AttendanceCorrection
                        {
                            Kind = AttendanceCorrectionKind.ChangePunchType,
                            NormalizedTransactionId = id,
                            OriginalEventType = punch.Type,
                            CorrectedEventType = newType,
                            ProposedValue = punch.Item.At,
                            TimeZoneId = punch.TimeZoneId,
                        });
                    }

                    break;

                case DayCorrectionRequest.Add:
                    if (ParseEventType(edit.EventType, out var addType) is { } addProblem)
                    {
                        problems.Add($"{label}: {addProblem}");
                    }
                    else if (edit.At is not { } at)
                    {
                        problems.Add($"{label}: say when the {addType} happened.");
                    }
                    else if (at.Date != day && !(data.View.HasOvernightShift && at.Date == day.AddDays(1)))
                    {
                        problems.Add($"{label}: the time must be on {day:yyyy-MM-dd}, the day being corrected"
                            + (data.View.HasOvernightShift ? ", or on the day after (the shift ends after midnight)." : "."));
                    }
                    else if (added.Contains((addType, at)))
                    {
                        problems.Add($"{label}: the same punch is added twice.");
                    }
                    else
                    {
                        added.Add((addType, at));
                    }

                    break;

                default:
                    problems.Add($"{label}: '{edit.Action}' is not something to do with a punch. Use Change, Ignore or Add.");
                    break;
            }
        }

        // An added punch is placed in the zone of the employee's punches around it, and can't be later than now.
        var actor = $"employee:{employeeId}";
        foreach (var (type, at) in added)
        {
            var unspecified = DateTime.SpecifyKind(at, DateTimeKind.Unspecified);
            var zone = await CorrectionTimeZones.ResolveAsync(_db, _audit, _clock, employeeId, unspecified, actor, cancellationToken);
            var instant = new VendorTimestamp(unspecified, null).ToUtc(zone);
            if (instant is null)
            {
                problems.Add($"The {type} at {at:HH:mm} is not a valid local time.");
            }
            else if (instant > _clock.GetUtcNow())
            {
                problems.Add($"The {type} at {at:HH:mm} is in the future.");
            }
            else
            {
                rows.Add(new AttendanceCorrection
                {
                    Kind = AttendanceCorrectionKind.SupplyPunch,
                    CorrectedEventType = type,
                    ProposedValue = unspecified,
                    TimeZoneId = zone,
                });
            }
        }

        if (problems.Count > 0)
        {
            errors[EditsKey] = string.Join(" ", problems);
        }
    }

    private async Task<DayData?> LoadAsync(int employeeId, DateTime day, CancellationToken cancellationToken)
    {
        var employee = await _db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            return null;
        }

        // The punches are a direct read of normalized attendance outside the engine: audited, and saved before the read.
        _audit.Record(AttendanceAuditActions.AttendanceDataRead, nameof(NormalizedAttendanceTransaction), null, $"Punches of employee {employeeId} on {day:yyyy-MM-dd}, for a correction.", $"employee:{employeeId}");
        await _db.SaveChangesAsync(cancellationToken);

        var shifts = (await _schedules.ResolveShiftsInRangeAsync(employeeId, day, day, cancellationToken))
            .Where(r => r.Date.Date == day)
            .SelectMany(r => r.Shifts)
            .OrderBy(s => s.StartsAt)
            .ToList();
        var overnight = shifts.Where(s => s.IsOvernight).ToList();
        var to = overnight.Count == 0 ? day.AddDays(1) : overnight.Max(s => s.EndsAt) + LateWindow;
        if (to < day.AddDays(1))
        {
            to = day.AddDays(1);
        }

        var rows = await _db.NormalizedAttendanceTransactions
            .AsNoTracking()
            .Where(n => n.EmployeeId == employeeId && n.EventTimestampLocal >= day && n.EventTimestampLocal < to)
            .OrderBy(n => n.EventTimestampLocal)
            .ThenBy(n => n.Id)
            .Select(n => new { n.Id, n.NormalizedEventType, n.EventTimestampLocal, n.TimeZoneId, n.DuplicateStatus, n.ProcessingStatus, n.ExceptionStatus })
            .ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();

        var applied = await _db.AttendanceCorrections
            .AsNoTracking()
            .Where(c => c.EmployeeId == employeeId
                && c.Status == AttendanceCorrectionStatus.Applied
                && ((c.NormalizedTransactionId != null && ids.Contains(c.NormalizedTransactionId.Value))
                    || (c.Kind == AttendanceCorrectionKind.SupplyPunch && c.ProposedValue >= day && c.ProposedValue < to)))
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);
        var ignored = applied.Where(c => c.Kind == AttendanceCorrectionKind.IgnorePunch).Select(c => c.NormalizedTransactionId!.Value).ToHashSet();
        var changed = applied
            .Where(c => c.Kind == AttendanceCorrectionKind.ChangePunchType)
            .GroupBy(c => c.NormalizedTransactionId!.Value)
            .ToDictionary(g => g.Key, g => g.Last().CorrectedEventType);

        var open = await _db.AttendanceExceptions
            .AsNoTracking()
            .Where(e => e.EmployeeId == employeeId && e.AttendanceDate == day && e.Status != AttendanceExceptionStatus.Closed)
            .ToListAsync(cancellationToken);
        var pointedAt = open
            .Where(e => e.ExceptionType == AttendanceExceptionType.InvalidSequence && e.RelatedNormalizedTransactionId != null)
            .GroupBy(e => e.RelatedNormalizedTransactionId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var punches = new Dictionary<int, PunchData>();
        var items = new List<DayPunchItem>();
        foreach (var row in rows)
        {
            var type = changed.TryGetValue(row.Id, out var corrected) ? corrected : row.NormalizedEventType;
            var state = ignored.Contains(row.Id) ? "Ignored"
                : row.DuplicateStatus != DuplicateStatus.Unique ? "Duplicate"
                : row.ProcessingStatus == NormalizedTransactionProcessingStatus.Excluded || row.ExceptionStatus == TransactionExceptionStatus.FlaggedUnrecognizedDevice || row.NormalizedEventType == NormalizedEventType.Other ? "NotCounted"
                : changed.ContainsKey(row.Id) ? "Changed"
                : "Counted";
            var editable = state is "Counted" or "Changed";
            pointedAt.TryGetValue(row.Id, out var problem);
            var item = new DayPunchItem
            {
                TransactionId = row.Id,
                Type = type.ToString(),
                OriginalType = state == "Changed" ? row.NormalizedEventType.ToString() : null,
                At = row.EventTimestampLocal,
                Source = "Device",
                State = state,
                Editable = editable,
                Problem = editable ? problem?.Description : null,
                SuggestedTypes = editable && problem is not null ? AttendanceExceptionRules.Suggested(problem.SuggestedEventTypes) : Array.Empty<string>(),
            };
            items.Add(item);
            punches[row.Id] = new PunchData(item, type, row.TimeZoneId);
        }

        items.AddRange(applied
            .Where(c => c.Kind == AttendanceCorrectionKind.SupplyPunch)
            .Select(c => new DayPunchItem { Type = c.CorrectedEventType.ToString(), At = c.ProposedValue, Source = "Corrected", State = "Added" }));
        items = items.OrderBy(i => i.At).ThenBy(i => i.TransactionId ?? int.MaxValue).ToList();

        var viewer = new ExceptionViewer(employeeId, true, false, false);
        var listed = await _exceptions.ListAsync(new ExceptionFilter(ExceptionStatusFilter.Active, day, day, employeeId, 100), viewer, cancellationToken);

        var pending = listed.Any(e => e.Status == nameof(AttendanceExceptionStatus.PendingCorrection))
            || await _db.AttendanceCorrections.AnyAsync(
                c => c.EmployeeId == employeeId
                    && c.AttendanceDate == day
                    && (c.Status == AttendanceCorrectionStatus.Pending || c.Status == AttendanceCorrectionStatus.Approved),
                cancellationToken);
        var anyOpen = listed.Any(e => e.Status == nameof(AttendanceExceptionStatus.Open));
        var reason = pending ? "A correction of this day is already waiting for a decision."
            : !anyOpen ? "This day has no exception to correct."
            : null;

        var view = new DayCorrectionView
        {
            Date = day,
            EmployeeId = employeeId,
            EmployeeName = $"{employee.FirstName} {employee.LastName}",
            Shifts = shifts.Select(Describe).ToList(),
            HasOvernightShift = overnight.Count > 0,
            Punches = items,
            Exceptions = listed,
            CanCorrect = reason is null,
            CannotCorrectReason = reason,
        };
        return new DayData(view, punches);
    }

    private sealed record PunchData(DayPunchItem Item, NormalizedEventType Type, string TimeZoneId);

    private sealed record DayData(DayCorrectionView View, Dictionary<int, PunchData> Punches);
}
