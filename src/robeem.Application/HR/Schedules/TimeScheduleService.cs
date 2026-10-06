using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.HR.Schedules;
using robeem.DataModel;

namespace robeem.Application.HR.Schedules;

public sealed class TimeScheduleService : ITimeScheduleService
{
    public const string DefaultTeamTitle = "All Employees";

    private const int MinutesPerDay = 1440;

    private readonly IRobeemDbContext _db;
    private readonly IChangeAuditTrail _audit;

    public TimeScheduleService(IRobeemDbContext db, IChangeAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<TimeScheduleListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.TimeSchedules
            .AsNoTracking()
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.Description,
                s.CyclePeriod,
                s.SystemSchedule,
                Shifts = _db.TimeScheduleShifts.Count(x => x.TimeScheduleId == s.Id),
                Teams = _db.TimeScheduleTeams.Count(x => x.TimeScheduleId == s.Id),
                Employees = _db.TimeScheduleEmployees.Count(x => x.TimeScheduleId == s.Id),
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id)
            .Select(r => new TimeScheduleListItem
            {
                Id = r.Id,
                Title = r.Title,
                Description = r.Description,
                CyclePeriod = r.CyclePeriod,
                ShiftCount = r.Shifts,
                TeamCount = r.Teams,
                EmployeeCount = r.Employees,
                IsSystemSchedule = r.SystemSchedule,
            })
            .ToList();
    }

    public async Task<TimeScheduleDetail?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var schedule = await _db.TimeSchedules.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (schedule is null)
        {
            return null;
        }

        var shifts = await _db.TimeScheduleShifts.AsNoTracking().Where(s => s.TimeScheduleId == id).ToListAsync(cancellationToken);
        var teams = await _db.TimeScheduleTeams.AsNoTracking().Where(t => t.TimeScheduleId == id).ToListAsync(cancellationToken);
        var lines = await _db.TimeScheduleLines.AsNoTracking().Where(l => l.TimeScheduleId == id).ToListAsync(cancellationToken);
        var employees = await _db.TimeScheduleEmployees
            .AsNoTracking()
            .Where(a => a.TimeScheduleId == id)
            .Select(a => new { a.EmployeeId, a.TeamId, a.Employee!.EmployeeNumber, a.Employee.FirstName, a.Employee.LastName })
            .ToListAsync(cancellationToken);

        return new TimeScheduleDetail
        {
            Id = schedule.Id,
            IsSystemSchedule = schedule.SystemSchedule,
            Settings = new TimeScheduleInput
            {
                Title = schedule.Title,
                Description = schedule.Description,
                CyclePeriod = schedule.CyclePeriod,
                FirstCycleDay = schedule.FirstCycleDay.ToString(),
                CycleStartDate = schedule.LastScheduleDate == DateTime.MinValue ? null : schedule.LastScheduleDate.Date,
            },
            Shifts = shifts
                .OrderBy(s => Set.ParseTime(s.StartTime)).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id)
                .Select(s => new ScheduleShiftItem
                {
                    Id = s.Id,
                    Title = s.Title,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    BreakTime = s.BreakTime,
                    BreakStartTime = s.BreakStartTime,
                    BreakEndTime = s.BreakEndTime,
                    Overtime = s.Overtime,
                    TotalHours = Math.Round(s.TotalHours, 2),
                    IsOvernight = Set.ParseTime(s.EndTime) < Set.ParseTime(s.StartTime),
                    RosterUses = lines.Count(l => l.ShiftId == s.Id),
                })
                .ToList(),
            Teams = teams
                .OrderByDescending(t => t.AllInclusive).ThenBy(t => t.Id)
                .Select(t => new ScheduleTeamItem
                {
                    Id = t.Id,
                    Title = t.Title,
                    AllInclusive = t.AllInclusive,
                    EmployeeCount = employees.Count(e => e.TeamId == t.Id),
                    Cells = lines
                        .Where(l => l.TeamId == t.Id)
                        .OrderBy(l => l.CycleDay).ThenBy(l => l.ShiftId)
                        .Select(l => new ScheduleRosterCell { CycleDay = l.CycleDay, ShiftId = l.ShiftId })
                        .ToList(),
                })
                .ToList(),
            Employees = employees
                .OrderBy(e => e.LastName, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.FirstName, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.EmployeeId)
                .Select(e => new ScheduleEmployeeItem { EmployeeId = e.EmployeeId, EmployeeNumber = e.EmployeeNumber, Name = $"{e.FirstName} {e.LastName}", TeamId = e.TeamId })
                .ToList(),
        };
    }

    public async Task<SaveResult> CreateAsync(TimeScheduleInput input, CancellationToken cancellationToken)
    {
        var errors = ValidateSettings(input, out var settings);
        if (errors.Count == 0 && await TitleTakenAsync(settings.Title, null, cancellationToken))
        {
            errors[nameof(TimeScheduleInput.Title)] = "Another time schedule has this name.";
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var schedule = new TimeSchedule();
        ApplySettings(schedule, settings);
        _db.TimeSchedules.Add(schedule);

        // Employees are assigned to a schedule's everyone-team, so a schedule is born with one.
        _db.TimeScheduleTeams.Add(new TimeScheduleTeam { TimeSchedule = schedule, Title = DefaultTeamTitle, AllInclusive = true });
        await _db.SaveChangesAsync(cancellationToken);

        Audit("Schedule", schedule.Id, $"Created '{schedule.Title}': {Describe(schedule)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(schedule.Id);
    }

    public async Task<SaveResult> UpdateAsync(int id, TimeScheduleInput input, CancellationToken cancellationToken)
    {
        var schedule = await _db.TimeSchedules.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (schedule is null)
        {
            return SaveResult.Missing();
        }

        var errors = ValidateSettings(input, out var settings);
        if (errors.Count == 0 && await TitleTakenAsync(settings.Title, id, cancellationToken))
        {
            errors[nameof(TimeScheduleInput.Title)] = "Another time schedule has this name.";
        }

        if (errors.Count == 0)
        {
            var beyond = await _db.TimeScheduleLines
                .Where(l => l.TimeScheduleId == id && l.CycleDay > settings.CyclePeriod)
                .Select(l => (int?)l.CycleDay)
                .MaxAsync(cancellationToken);
            if (beyond is { } day)
            {
                errors[nameof(TimeScheduleInput.CyclePeriod)] = $"The roster still has entries on day {day}. Remove the entries after day {settings.CyclePeriod} first.";
            }
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = $"'{schedule.Title}': {Describe(schedule)}";
        ApplySettings(schedule, settings);
        Audit("Schedule", id, $"Changed. Before: {before}. After: '{schedule.Title}': {Describe(schedule)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(id);
    }

    public async Task<SaveResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var schedule = await _db.TimeSchedules.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (schedule is null)
        {
            return SaveResult.Missing();
        }

        if (schedule.SystemSchedule)
        {
            return SaveResult.Invalid("id", "This schedule belongs to the system and can't be deleted.");
        }

        var assigned = await _db.TimeScheduleEmployees.CountAsync(a => a.TimeScheduleId == id, cancellationToken);
        if (assigned > 0)
        {
            return SaveResult.Invalid("id", $"{assigned} employee(s) are assigned to this schedule. Move them to another schedule first.");
        }

        _db.TimeScheduleLines.RemoveRange(await _db.TimeScheduleLines.Where(l => l.TimeScheduleId == id).ToListAsync(cancellationToken));
        _db.TimeScheduleTeams.RemoveRange(await _db.TimeScheduleTeams.Where(t => t.TimeScheduleId == id).ToListAsync(cancellationToken));
        _db.TimeScheduleShifts.RemoveRange(await _db.TimeScheduleShifts.Where(s => s.TimeScheduleId == id).ToListAsync(cancellationToken));
        _db.TimeSchedules.Remove(schedule);
        Audit("Schedule", id, $"Deleted '{schedule.Title}'.");

        return await SaveOrRefuseAsync(id, "This schedule is still used: an attendance policy is set for it, or attendance records and exceptions were calculated against its shifts. Keep it.", cancellationToken);
    }

    public async Task<SaveResult> AddShiftAsync(int scheduleId, ScheduleShiftInput input, CancellationToken cancellationToken)
    {
        if (!await _db.TimeSchedules.AnyAsync(s => s.Id == scheduleId, cancellationToken))
        {
            return SaveResult.Missing();
        }

        var errors = await ValidateShiftAsync(scheduleId, null, input, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var shift = new TimeScheduleShift { TimeScheduleId = scheduleId };
        ApplyShift(shift, input);
        _db.TimeScheduleShifts.Add(shift);
        await _db.SaveChangesAsync(cancellationToken);

        Audit("Shift", shift.Id, $"Shift {Describe(shift)} added to schedule {scheduleId}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(shift.Id);
    }

    public async Task<SaveResult> UpdateShiftAsync(int scheduleId, int shiftId, ScheduleShiftInput input, CancellationToken cancellationToken)
    {
        var shift = await _db.TimeScheduleShifts.SingleOrDefaultAsync(s => s.Id == shiftId && s.TimeScheduleId == scheduleId, cancellationToken);
        if (shift is null)
        {
            return SaveResult.Missing();
        }

        var errors = await ValidateShiftAsync(scheduleId, shiftId, input, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = Describe(shift);
        ApplyShift(shift, input);
        Audit("Shift", shiftId, $"Shift changed in schedule {scheduleId}. Before: {before}. After: {Describe(shift)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(shiftId);
    }

    public async Task<SaveResult> DeleteShiftAsync(int scheduleId, int shiftId, CancellationToken cancellationToken)
    {
        var shift = await _db.TimeScheduleShifts.SingleOrDefaultAsync(s => s.Id == shiftId && s.TimeScheduleId == scheduleId, cancellationToken);
        if (shift is null)
        {
            return SaveResult.Missing();
        }

        var uses = await _db.TimeScheduleLines.CountAsync(l => l.ShiftId == shiftId, cancellationToken);
        if (uses > 0)
        {
            return SaveResult.Invalid("id", $"The roster uses this shift on {uses} day(s). Take it off the roster first.");
        }

        _db.TimeScheduleShifts.Remove(shift);
        Audit("Shift", shiftId, $"Shift {Describe(shift)} deleted from schedule {scheduleId}.");
        return await SaveOrRefuseAsync(shiftId, "Attendance records were calculated against this shift, so it can't be deleted. Rename it if it is retired.", cancellationToken);
    }

    public async Task<SaveResult> AddTeamAsync(int scheduleId, ScheduleTeamInput input, CancellationToken cancellationToken)
    {
        if (!await _db.TimeSchedules.AnyAsync(s => s.Id == scheduleId, cancellationToken))
        {
            return SaveResult.Missing();
        }

        var errors = await ValidateTeamAsync(scheduleId, null, input, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var team = new TimeScheduleTeam { TimeScheduleId = scheduleId, Title = input.Title.Trim(), AllInclusive = false };
        _db.TimeScheduleTeams.Add(team);
        await _db.SaveChangesAsync(cancellationToken);

        Audit("Team", team.Id, $"Team '{team.Title}' added to schedule {scheduleId}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(team.Id);
    }

    public async Task<SaveResult> RenameTeamAsync(int scheduleId, int teamId, ScheduleTeamInput input, CancellationToken cancellationToken)
    {
        var team = await _db.TimeScheduleTeams.SingleOrDefaultAsync(t => t.Id == teamId && t.TimeScheduleId == scheduleId, cancellationToken);
        if (team is null)
        {
            return SaveResult.Missing();
        }

        var errors = await ValidateTeamAsync(scheduleId, teamId, input, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = team.Title;
        team.Title = input.Title.Trim();
        Audit("Team", teamId, $"Team renamed in schedule {scheduleId} from '{before}' to '{team.Title}'.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(teamId);
    }

    public async Task<SaveResult> DeleteTeamAsync(int scheduleId, int teamId, CancellationToken cancellationToken)
    {
        var team = await _db.TimeScheduleTeams.SingleOrDefaultAsync(t => t.Id == teamId && t.TimeScheduleId == scheduleId, cancellationToken);
        if (team is null)
        {
            return SaveResult.Missing();
        }

        if (team.AllInclusive)
        {
            return SaveResult.Invalid("id", "The team that contains everyone on the schedule can't be deleted: employees are assigned to it.");
        }

        // Its members work the everyone-team's roster again.
        var members = await _db.TimeScheduleEmployees.Where(a => a.TeamId == teamId).ToListAsync(cancellationToken);
        if (members.Count > 0)
        {
            var defaultTeamId = await DefaultTeamIdAsync(scheduleId, cancellationToken);
            if (defaultTeamId is null)
            {
                return SaveResult.Invalid("id", "The schedule has no team that contains everyone, so the team's members have nowhere to go.");
            }

            foreach (var member in members)
            {
                member.TeamId = defaultTeamId.Value;
            }
        }

        _db.TimeScheduleLines.RemoveRange(await _db.TimeScheduleLines.Where(l => l.TeamId == teamId).ToListAsync(cancellationToken));
        _db.TimeScheduleTeams.Remove(team);
        Audit("Team", teamId, $"Team '{team.Title}' deleted from schedule {scheduleId}; {members.Count} member(s) went back to the everyone-team.");
        return await SaveOrRefuseAsync(teamId, "The team is still in use. Keep it.", cancellationToken);
    }

    public async Task<SaveResult> SaveRosterAsync(int scheduleId, int teamId, ScheduleRosterInput input, CancellationToken cancellationToken)
    {
        var schedule = await _db.TimeSchedules.AsNoTracking().SingleOrDefaultAsync(s => s.Id == scheduleId, cancellationToken);
        if (schedule is null || !await _db.TimeScheduleTeams.AnyAsync(t => t.Id == teamId && t.TimeScheduleId == scheduleId, cancellationToken))
        {
            return SaveResult.Missing();
        }

        var shifts = await _db.TimeScheduleShifts.AsNoTracking().Where(s => s.TimeScheduleId == scheduleId).ToListAsync(cancellationToken);
        var wanted = new HashSet<(int Day, int Shift)>();
        foreach (var cell in input.Cells ?? [])
        {
            if (cell.CycleDay < 1 || cell.CycleDay > schedule.CyclePeriod)
            {
                return SaveResult.Invalid(nameof(ScheduleRosterInput.Cells), $"Day {cell.CycleDay} is outside the {schedule.CyclePeriod}-day pattern.");
            }

            if (shifts.All(s => s.Id != cell.ShiftId))
            {
                return SaveResult.Invalid(nameof(ScheduleRosterInput.Cells), "A chosen shift doesn't belong to this schedule.");
            }

            wanted.Add((cell.CycleDay, cell.ShiftId));
        }

        foreach (var day in wanted.GroupBy(w => w.Day).OrderBy(g => g.Key))
        {
            var overlap = FindOverlap(day.Select(d => shifts.Single(s => s.Id == d.Shift)).ToList());
            if (overlap is { } pair)
            {
                return SaveResult.Invalid(nameof(ScheduleRosterInput.Cells), $"On day {day.Key}, '{pair.First}' and '{pair.Second}' overlap. A day's shifts can't run at the same time.");
            }
        }

        var current = await _db.TimeScheduleLines.Where(l => l.TeamId == teamId).ToListAsync(cancellationToken);
        var removed = current.Where(l => !wanted.Contains((l.CycleDay, l.ShiftId))).ToList();
        var added = wanted.Where(w => current.All(l => (l.CycleDay, l.ShiftId) != w)).ToList();
        if (removed.Count == 0 && added.Count == 0)
        {
            return SaveResult.Success(teamId);
        }

        _db.TimeScheduleLines.RemoveRange(removed);
        _db.TimeScheduleLines.AddRange(added.Select(w => new TimeScheduleLine { TimeScheduleId = scheduleId, TeamId = teamId, CycleDay = w.Day, ShiftId = w.Shift }));
        Audit("Team", teamId, $"Roster of team {teamId} in schedule {scheduleId} changed: {added.Count} entr{(added.Count == 1 ? "y" : "ies")} added, {removed.Count} removed; {wanted.Select(w => w.Day).Distinct().Count()} working day(s) of {schedule.CyclePeriod}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(teamId);
    }

    public async Task<SaveResult> SetTeamMembersAsync(int scheduleId, int teamId, ScheduleMembersInput input, CancellationToken cancellationToken)
    {
        var team = await _db.TimeScheduleTeams.AsNoTracking().SingleOrDefaultAsync(t => t.Id == teamId && t.TimeScheduleId == scheduleId, cancellationToken);
        if (team is null)
        {
            return SaveResult.Missing();
        }

        if (team.AllInclusive)
        {
            return SaveResult.Invalid(nameof(ScheduleMembersInput.EmployeeIds), "This team contains everyone on the schedule; it has no chosen members.");
        }

        var defaultTeamId = await DefaultTeamIdAsync(scheduleId, cancellationToken);
        if (defaultTeamId is null)
        {
            return SaveResult.Invalid(nameof(ScheduleMembersInput.EmployeeIds), "The schedule has no team that contains everyone.");
        }

        var wanted = (input.EmployeeIds ?? []).Distinct().ToList();
        var onSchedule = await _db.TimeScheduleEmployees.Where(a => a.TimeScheduleId == scheduleId).ToListAsync(cancellationToken);
        if (wanted.Any(id => onSchedule.All(a => a.EmployeeId != id)))
        {
            return SaveResult.Invalid(nameof(ScheduleMembersInput.EmployeeIds), "Only employees assigned to this schedule can be in its teams. Assign them to the schedule first.");
        }

        var changed = 0;
        foreach (var assignment in onSchedule)
        {
            var target = wanted.Contains(assignment.EmployeeId) ? teamId : (assignment.TeamId == teamId ? defaultTeamId.Value : assignment.TeamId);
            if (assignment.TeamId != target)
            {
                assignment.TeamId = target;
                changed++;
            }
        }

        if (changed > 0)
        {
            Audit("Team", teamId, $"Members of team '{team.Title}' in schedule {scheduleId} set to {wanted.Count} employee(s) ({changed} moved).");
            await _db.SaveChangesAsync(cancellationToken);
        }

        return SaveResult.Success(teamId);
    }

    private static Dictionary<string, string> ValidateSettings(TimeScheduleInput input, out ScheduleSettings settings)
    {
        var errors = new Dictionary<string, string>();
        var title = (input.Title ?? string.Empty).Trim();
        if (title.Length == 0 || title.Length > 255)
        {
            errors[nameof(TimeScheduleInput.Title)] = "A name is required (at most 255 characters).";
        }

        var description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        if (description is { Length: > 1000 })
        {
            errors[nameof(TimeScheduleInput.Description)] = "The description can be at most 1000 characters.";
        }

        if (input.CyclePeriod is < 1 or > 56)
        {
            errors[nameof(TimeScheduleInput.CyclePeriod)] = "The pattern is between 1 and 56 days long.";
        }

        var first = DayOfWeek.Monday;
        if (input.CyclePeriod == 7 && (!Enum.TryParse(input.FirstCycleDay, ignoreCase: true, out first) || !Enum.IsDefined(first)))
        {
            errors[nameof(TimeScheduleInput.FirstCycleDay)] = $"Choose the weekday the week starts on: {string.Join(", ", Enum.GetNames<DayOfWeek>())}.";
        }

        if (input.CyclePeriod != 7 && input.CycleStartDate is null)
        {
            errors[nameof(TimeScheduleInput.CycleStartDate)] = "Give the date that is day 1 of the pattern: only a weekly pattern can start on a weekday.";
        }

        settings = new ScheduleSettings(title, description, input.CyclePeriod, first, input.CycleStartDate);
        return errors;
    }

    /// <summary>
    /// A 7-day pattern is anchored to a weekday, as the seeded schedule is. Any other length is
    /// anchored to the date of its day 1, because <c>TimeSchedule.GetCycleDay</c> counts from
    /// <c>LastScheduleDate</c>; without it a 14-day pattern would never get past day 7.
    /// </summary>
    private static void ApplySettings(TimeSchedule schedule, ScheduleSettings settings)
    {
        schedule.Title = settings.Title;
        schedule.Description = settings.Description;
        schedule.CyclePeriod = settings.CyclePeriod;
        schedule.LastScheduleDay = 1;
        if (settings.CyclePeriod == 7)
        {
            schedule.FirstCycleDay = settings.FirstCycleDay;
            schedule.LastScheduleDate = DateTime.MinValue;
        }
        else
        {
            var start = DateTime.SpecifyKind(settings.CycleStartDate!.Value.Date, DateTimeKind.Unspecified);
            schedule.FirstCycleDay = start.DayOfWeek;
            schedule.LastScheduleDate = start;
        }
    }

    private static string Describe(TimeSchedule schedule) =>
        schedule.CyclePeriod == 7 && schedule.LastScheduleDate == DateTime.MinValue
            ? $"7-day pattern, week starts on {schedule.FirstCycleDay}"
            : $"{schedule.CyclePeriod}-day pattern, day 1 on {schedule.LastScheduleDate:yyyy-MM-dd}";

    private static string Describe(TimeScheduleShift shift) =>
        $"'{shift.Title}' {shift.StartTime}-{shift.EndTime}" + (shift.BreakTime ? $", break {shift.BreakStartTime}-{shift.BreakEndTime}" : string.Empty) + (shift.Overtime ? ", overtime" : string.Empty);

    private static void ApplyShift(TimeScheduleShift shift, ScheduleShiftInput input)
    {
        TryTime(input.StartTime, out var start, out _);
        TryTime(input.EndTime, out var end, out _);
        shift.Title = input.Title.Trim();
        shift.StartTime = start;
        shift.EndTime = end;
        shift.Overtime = input.Overtime;
        shift.BreakTime = input.BreakTime;
        if (input.BreakTime)
        {
            TryTime(input.BreakStartTime, out var breakStart, out _);
            TryTime(input.BreakEndTime, out var breakEnd, out _);
            shift.BreakStartTime = breakStart;
            shift.BreakEndTime = breakEnd;
        }
        else
        {
            shift.BreakStartTime = "00:00";
            shift.BreakEndTime = "00:00";
        }
    }

    /// <summary>Returns the first two shifts of a day that run at the same time, or null.</summary>
    private static (string First, string Second)? FindOverlap(IReadOnlyList<TimeScheduleShift> shifts)
    {
        var spans = shifts
            .Select(s =>
            {
                var start = (int)Set.ParseTime(s.StartTime).TotalMinutes;
                var end = (int)Set.ParseTime(s.EndTime).TotalMinutes;
                return (s.Title, Start: start, End: end <= start ? end + MinutesPerDay : end);
            })
            .OrderBy(s => s.Start)
            .ToList();

        for (var i = 1; i < spans.Count; i++)
        {
            if (spans[i].Start < spans[i - 1].End)
            {
                return (spans[i - 1].Title, spans[i].Title);
            }
        }

        return null;
    }

    private static bool TryTime(string? text, out string normalized, out int minutes)
    {
        var trimmed = text?.Trim();
        if (!Set.IsValidTime(trimmed))
        {
            (normalized, minutes) = (string.Empty, 0);
            return false;
        }

        var span = Set.ParseTime(trimmed!);
        normalized = $"{span.Hours:00}:{span.Minutes:00}";
        minutes = (int)span.TotalMinutes;
        return true;
    }

    /// <summary>Minutes from <paramref name="origin"/> forward to <paramref name="time"/>, within one day.</summary>
    private static int Offset(int time, int origin) => (((time - origin) % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;

    /// <summary>The team employees are assigned to: the lowest-numbered one that contains everyone (<c>EmployeeService</c> does the same).</summary>
    private async Task<int?> DefaultTeamIdAsync(int scheduleId, CancellationToken cancellationToken) =>
        await _db.TimeScheduleTeams
            .Where(t => t.TimeScheduleId == scheduleId && t.AllInclusive)
            .OrderBy(t => t.Id)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<bool> TitleTakenAsync(string title, int? exceptId, CancellationToken cancellationToken)
    {
        var lower = title.ToLower();
        return await _db.TimeSchedules.AnyAsync(s => s.Id != exceptId && s.Title.ToLower() == lower, cancellationToken);
    }

    private async Task<Dictionary<string, string>> ValidateShiftAsync(int scheduleId, int? existingId, ScheduleShiftInput input, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var title = (input.Title ?? string.Empty).Trim();
        if (title.Length == 0 || title.Length > 255)
        {
            errors[nameof(ScheduleShiftInput.Title)] = "A name is required (at most 255 characters).";
        }
        else
        {
            var lower = title.ToLower();
            if (await _db.TimeScheduleShifts.AnyAsync(s => s.TimeScheduleId == scheduleId && s.Id != existingId && s.Title.ToLower() == lower, cancellationToken))
            {
                errors[nameof(ScheduleShiftInput.Title)] = "Another shift of this schedule has this name.";
            }
        }

        var startOk = TryTime(input.StartTime, out _, out var start);
        var endOk = TryTime(input.EndTime, out _, out var end);
        if (!startOk)
        {
            errors[nameof(ScheduleShiftInput.StartTime)] = "Give the start as a time of day, such as 09:00.";
        }

        if (!endOk)
        {
            errors[nameof(ScheduleShiftInput.EndTime)] = "Give the end as a time of day, such as 17:00. An end earlier than the start is an overnight shift.";
        }

        if (startOk && endOk && start == end)
        {
            errors[nameof(ScheduleShiftInput.EndTime)] = "The shift can't start and end at the same time.";
        }

        if (input.BreakTime && startOk && endOk && start != end)
        {
            var breakStartOk = TryTime(input.BreakStartTime, out _, out var breakStart);
            var breakEndOk = TryTime(input.BreakEndTime, out _, out var breakEnd);
            if (!breakStartOk)
            {
                errors[nameof(ScheduleShiftInput.BreakStartTime)] = "Give the break's start as a time of day, such as 13:00.";
            }

            if (!breakEndOk)
            {
                errors[nameof(ScheduleShiftInput.BreakEndTime)] = "Give the break's end as a time of day, such as 14:00.";
            }

            if (breakStartOk && breakEndOk)
            {
                // Measured from the start of the shift, so a break in an overnight shift works.
                var length = Offset(end, start);
                var from = Offset(breakStart, start);
                var to = Offset(breakEnd, start);
                if (to == 0)
                {
                    to = MinutesPerDay;
                }

                if (from >= to || to > length)
                {
                    errors[nameof(ScheduleShiftInput.BreakStartTime)] = "The break must fall inside the shift, and end after it starts.";
                }
            }
        }

        return errors;
    }

    private async Task<Dictionary<string, string>> ValidateTeamAsync(int scheduleId, int? existingId, ScheduleTeamInput input, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var title = (input.Title ?? string.Empty).Trim();
        if (title.Length == 0 || title.Length > 255)
        {
            errors[nameof(ScheduleTeamInput.Title)] = "A name is required (at most 255 characters).";
        }
        else
        {
            var lower = title.ToLower();
            if (await _db.TimeScheduleTeams.AnyAsync(t => t.TimeScheduleId == scheduleId && t.Id != existingId && t.Title.ToLower() == lower, cancellationToken))
            {
                errors[nameof(ScheduleTeamInput.Title)] = "Another team of this schedule has this name.";
            }
        }

        return errors;
    }

    private void Audit(string entityType, int entityId, string details) =>
        _audit.Record(ChangeAuditActions.ScheduleChanged, entityType, entityId, details);

    /// <summary>Saves; when the database refuses because something still refers to a deleted row, says so and keeps everything.</summary>
    private async Task<SaveResult> SaveOrRefuseAsync(int id, string refusal, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A restricted foreign key (a policy, attendance records or exceptions): the whole change is undone.
            _db.ChangeTracker.Clear();
            return SaveResult.Invalid("id", refusal);
        }

        return SaveResult.Success(id);
    }

    private sealed record ScheduleSettings(string Title, string? Description, int CyclePeriod, DayOfWeek FirstCycleDay, DateTime? CycleStartDate);
}
