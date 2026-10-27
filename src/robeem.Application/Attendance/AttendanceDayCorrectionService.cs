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
        NormalizedEventType.BreakIn,
        NormalizedEventType.BreakOut,
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
        if (justification.Length > JustificationMaxLength)
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
            if (errors.Count == 0)
            {
                var after = PunchesAfter(data, rows);
                if (MissingAfter(after, data) is { Count: > 0 } missing)
                {
                    errors[EditsKey] = $"The day would still be incomplete: it needs {string.Join(", ", missing)}. Change, add or keep punches so that every one is there.";
                }
                else if (OutOfOrder(after) is { } order)
                {
                    errors[EditsKey] = order;
                }
            }
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

    /// <summary>
    /// Gets what the day still lacks once the edits are applied: a required punch type that no counted punch has, once for each shift that
    /// needs one, and the partner of a check-in, check-out, break-out or break-in that has none. Counted punches are the day's punches that count, as the edits leave them, and the ones earlier corrections added.
    /// </summary>
    private static List<string> MissingAfter(List<(NormalizedEventType Type, DateTime At)> punches, DayData data)
    {
        var types = punches.Select(p => p.Type).ToList();

        // A punch never stands alone: a check-in needs a check-out and the other way round, and the same for the break pair, whatever the
        // shift or policy asks. So a type is needed as many times as its partner is there.
        int Count(NormalizedEventType type) => types.Count(t => t == type);
        var partners = new Dictionary<NormalizedEventType, NormalizedEventType>
        {
            [NormalizedEventType.CheckIn] = NormalizedEventType.CheckOut,
            [NormalizedEventType.CheckOut] = NormalizedEventType.CheckIn,
            [NormalizedEventType.BreakOut] = NormalizedEventType.BreakIn,
            [NormalizedEventType.BreakIn] = NormalizedEventType.BreakOut,
        };

        return AttendanceTypes
            .Where(type => Count(type) < Math.Max(data.Needed.GetValueOrDefault(type), Count(partners[type])))
            .Select(DescribeType)
            .ToList();
    }

    /// <summary>The punches that count on the day once the edits are applied: the ones that count now, as the edits leave them, and the added ones.</summary>
    private static List<(NormalizedEventType Type, DateTime At)> PunchesAfter(DayData data, List<AttendanceCorrection> rows)
    {
        var ignored = rows.Where(r => r.Kind == AttendanceCorrectionKind.IgnorePunch).Select(r => r.NormalizedTransactionId!.Value).ToHashSet();
        var retyped = rows.Where(r => r.Kind == AttendanceCorrectionKind.ChangePunchType).ToDictionary(r => r.NormalizedTransactionId!.Value, r => r.CorrectedEventType);

        var deleted = rows.Where(r => r.Kind == AttendanceCorrectionKind.CancelSuppliedPunch).Select(r => r.CancelsCorrectionId!.Value).ToHashSet();

        var punches = new List<(NormalizedEventType Type, DateTime At)>();
        foreach (var item in data.View.Punches.Where(p => p.State is "Counted" or "Changed" or "Added"))
        {
            if (item.CorrectionId is { } correctionId && deleted.Contains(correctionId))
            {
                continue;
            }

            if (item.TransactionId is { } id && data.Punches.TryGetValue(id, out var punch))
            {
                if (!ignored.Contains(id))
                {
                    punches.Add((retyped.TryGetValue(id, out var newType) ? newType : punch.Type, item.At));
                }
            }
            else if (Enum.TryParse<NormalizedEventType>(item.Type, out var addedType))
            {
                punches.Add((addedType, item.At));
            }
        }

        punches.AddRange(rows.Where(r => r.Kind == AttendanceCorrectionKind.SupplyPunch).Select(r => (r.CorrectedEventType, r.ProposedValue)));
        return punches;
    }

    /// <summary>
    /// Gets what is out of order in the punches, in time: after a check-in comes a break-in or a check-out, after a break-in a break-out,
    /// after a break-out a break-in or a check-out, and a new check-in only after a check-out. Null when they follow that order and the day ends checked out.
    /// </summary>
    private static string? OutOfOrder(List<(NormalizedEventType Type, DateTime At)> punches)
    {
        // Punches at the same minute are taken in the natural order, so a tap pair 08:00 / 08:00 is not judged by the order they were stored in.
        var ordered = punches.OrderBy(p => p.At).ThenBy(p => Array.IndexOf(AttendanceTypes, p.Type)).ToList();
        var previous = default(NormalizedEventType?);
        foreach (var (type, at) in ordered)
        {
            var allowed = previous switch
            {
                null or NormalizedEventType.CheckOut => type == NormalizedEventType.CheckIn,
                NormalizedEventType.CheckIn or NormalizedEventType.BreakOut => type is NormalizedEventType.BreakIn or NormalizedEventType.CheckOut,
                _ => type == NormalizedEventType.BreakOut,
            };
            if (!allowed)
            {
                return $"The punches are out of order: {DescribeType(type)} at {at:HH:mm} can't come {(previous is null ? "first" : $"after {DescribeType(previous.Value)}")}. "
                    + "The order is check-in, break-in, break-out, check-out.";
            }

            previous = type;
        }

        return previous is null or NormalizedEventType.CheckOut ? null : "The punches are out of order: the day must end with a check-out.";
    }

    private static string DescribeType(NormalizedEventType type) => type switch
    {
        NormalizedEventType.CheckIn => "a check-in",
        NormalizedEventType.CheckOut => "a check-out",
        NormalizedEventType.BreakOut => "a break-out",
        _ => "a break-in",
    };

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
        var taken = new HashSet<int>();
        var added = new List<(NormalizedEventType Type, DateTime At)>();
        foreach (var (edit, index) in request.Edits.Select((e, i) => (e, i + 1)))
        {
            var label = $"Edit {index}";
            switch (edit.Action)
            {
                case DayCorrectionRequest.Ignore when edit.CorrectionId is { } correctionId:
                    // Deleting a punch that an earlier correction added: a new correction takes it back, the earlier one is not edited.
                    if (!data.Supplied.TryGetValue(correctionId, out var supplied))
                    {
                        problems.Add($"{label}: that is not a punch added to this day by a correction, so it can't be deleted.");
                    }
                    else if (!taken.Add(correctionId))
                    {
                        problems.Add($"{label}: the added punch at {supplied.At:HH:mm} is already deleted by another edit.");
                    }
                    else
                    {
                        rows.Add(new AttendanceCorrection
                        {
                            Kind = AttendanceCorrectionKind.CancelSuppliedPunch,
                            CancelsCorrectionId = correctionId,
                            OriginalEventType = supplied.Type,
                            CorrectedEventType = supplied.Type,
                            ProposedValue = supplied.At,
                            TimeZoneId = supplied.TimeZoneId,
                        });
                    }

                    break;

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

        // A punch an earlier correction added can be deleted by a new one, unless one already took it back.
        var suppliedIds = applied.Where(c => c.Kind == AttendanceCorrectionKind.SupplyPunch).Select(c => c.Id).ToList();
        var cancelledIds = await _db.AttendanceCorrections
            .AsNoTracking()
            .Where(c => c.EmployeeId == employeeId
                && c.Status == AttendanceCorrectionStatus.Applied
                && c.Kind == AttendanceCorrectionKind.CancelSuppliedPunch
                && c.CancelsCorrectionId != null
                && suppliedIds.Contains(c.CancelsCorrectionId.Value))
            .Select(c => c.CancelsCorrectionId!.Value)
            .ToListAsync(cancellationToken);

        var supplied = new Dictionary<int, SuppliedData>();
        foreach (var c in applied.Where(c => c.Kind == AttendanceCorrectionKind.SupplyPunch))
        {
            var taken = cancelledIds.Contains(c.Id);
            items.Add(new DayPunchItem
            {
                CorrectionId = taken ? null : c.Id,
                Type = c.CorrectedEventType.ToString(),
                At = c.ProposedValue,
                Source = "Corrected",
                State = taken ? "Ignored" : "Added",
            });
            if (!taken)
            {
                supplied[c.Id] = new SuppliedData(c.CorrectedEventType, c.ProposedValue, c.TimeZoneId);
            }
        }

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

        // Every shift needs a check-in and a check-out (a day with no schedule, one); a shift with a break needs the pair of break punches
        // when the policy asks for them, the same rule that makes a shift incomplete when it is calculated.
        var policy = await _policies.ResolveAsync(employeeId, cancellationToken);
        var shiftCount = Math.Max(1, shifts.Count);
        var breakCount = policy.RaiseExceptionOnNoBreak ? shifts.Count(s => s.BreakTime) : 0;
        var needed = new Dictionary<NormalizedEventType, int>
        {
            [NormalizedEventType.CheckIn] = shiftCount,
            [NormalizedEventType.BreakOut] = breakCount,
            [NormalizedEventType.BreakIn] = breakCount,
            [NormalizedEventType.CheckOut] = shiftCount,
        };

        var view = new DayCorrectionView
        {
            Date = day,
            EmployeeId = employeeId,
            EmployeeName = $"{employee.FirstName} {employee.LastName}",
            Shifts = shifts.Select(Describe).ToList(),
            HasOvernightShift = overnight.Count > 0,
            RequiredPunches = AttendanceTypes.Where(t => needed[t] > 0).Select(t => t.ToString()).ToList(),
            Punches = items,
            Exceptions = listed,
            CanCorrect = reason is null,
            CannotCorrectReason = reason,
        };
        return new DayData(view, punches, supplied, needed);
    }

    private sealed record PunchData(DayPunchItem Item, NormalizedEventType Type, string TimeZoneId);

    private sealed record SuppliedData(NormalizedEventType Type, DateTime At, string TimeZoneId);

    private sealed record DayData(DayCorrectionView View, Dictionary<int, PunchData> Punches, Dictionary<int, SuppliedData> Supplied, Dictionary<NormalizedEventType, int> Needed);
}
