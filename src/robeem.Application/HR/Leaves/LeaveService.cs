using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Leaves;
using robeem.DataModel;

namespace robeem.Application.HR.Leaves;

/// <summary>
/// Leaves of the tenant's employees. HR saves a leave already approved. The list is paged on the server (CLAUDE.md §2.9).
/// </summary>
public sealed class LeaveService : ILeaveService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;
    private const int MaxDays = 366;
    private const int NotesMaxLength = 500;

    private readonly IRobeemDbContext _db;

    public LeaveService(IRobeemDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<LeaveListItem>> ListAsync(LeaveFilter filter, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(filter.PageSize <= 0 ? DefaultPageSize : filter.PageSize, 1, MaxPageSize);
        var leaves = _db.EmployeeLeaves.AsNoTracking();

        if (filter.EmployeeId is { } employeeId)
        {
            leaves = leaves.Where(l => l.EmployeeId == employeeId);
        }

        if (filter.From is { } from)
        {
            var first = from.Date;
            leaves = leaves.Where(l => l.EndDate >= first);
        }

        if (filter.To is { } to)
        {
            var last = to.Date;
            leaves = leaves.Where(l => l.StartDate <= last);
        }

        if (!string.IsNullOrWhiteSpace(filter.LeaveType) && Enum.TryParse<LeaveType>(filter.LeaveType, true, out var type))
        {
            leaves = leaves.Where(l => l.LeaveType == type);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // PostgreSQL compares text case-sensitively; lower-case both sides so search is not.
            var term = filter.Search.Trim().ToLowerInvariant();
            leaves = leaves.Where(l =>
                l.Employee!.EmployeeNumber.ToLower().Contains(term)
                || l.Employee.FirstName.ToLower().Contains(term)
                || l.Employee.LastName.ToLower().Contains(term));
        }

        // The total is counted with the same filters as the page; a page past the end is answered with the last page.
        var total = await leaves.CountAsync(cancellationToken);
        var current = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)size)));

        // The id is unique, so the order is total and pages never overlap.
        var rows = await leaves
            .OrderByDescending(l => l.StartDate)
            .ThenByDescending(l => l.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .Select(l => new
            {
                l.Id,
                l.EmployeeId,
                l.Employee!.EmployeeNumber,
                l.Employee.FirstName,
                l.Employee.LastName,
                l.LeaveType,
                l.StartDate,
                l.EndDate,
                l.IsPartDay,
                l.StartTime,
                l.EndTime,
                l.Notes,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<LeaveListItem>
        {
            Items = rows.Select(r => new LeaveListItem
            {
                Id = r.Id,
                EmployeeId = r.EmployeeId,
                EmployeeNumber = r.EmployeeNumber,
                EmployeeName = $"{r.FirstName} {r.LastName}".Trim(),
                LeaveType = r.LeaveType.ToString(),
                StartDate = r.StartDate.Date,
                EndDate = r.EndDate.Date,
                IsPartDay = r.IsPartDay,
                StartTime = r.StartTime,
                EndTime = r.EndTime,
                Notes = r.Notes,
            }).ToList(),
            Total = total,
            Page = current,
            PageSize = size,
        };
    }

    public async Task<LeaveListItem?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var l = await _db.EmployeeLeaves.AsNoTracking().Include(x => x.Employee).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return l is null
            ? null
            : new LeaveListItem
            {
                Id = l.Id,
                EmployeeId = l.EmployeeId,
                EmployeeNumber = l.Employee!.EmployeeNumber,
                EmployeeName = l.Employee.FullName,
                LeaveType = l.LeaveType.ToString(),
                StartDate = l.StartDate.Date,
                EndDate = l.EndDate.Date,
                IsPartDay = l.IsPartDay,
                StartTime = l.StartTime,
                EndTime = l.EndTime,
                Notes = l.Notes,
            };
    }

    public async Task<SaveResult> CreateAsync(LeaveInput input, CancellationToken cancellationToken)
    {
        var leave = new EmployeeLeave();
        var errors = await ApplyAsync(input, leave, null, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        _db.EmployeeLeaves.Add(leave);
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(leave.Id);
    }

    public async Task<SaveResult> UpdateAsync(int id, LeaveInput input, CancellationToken cancellationToken)
    {
        var leave = await _db.EmployeeLeaves.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (leave is null)
        {
            return SaveResult.Missing();
        }

        var errors = await ApplyAsync(input, leave, id, cancellationToken);
        if (errors.Count > 0)
        {
            _db.ChangeTracker.Clear();
            return SaveResult.Invalid(errors);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(id);
    }

    public async Task<SaveResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var leave = await _db.EmployeeLeaves.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (leave is null)
        {
            return SaveResult.Missing();
        }

        _db.EmployeeLeaves.Remove(leave);
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(id);
    }

    private static (DateTime Start, DateTime End) Interval(DateTime startDate, DateTime endDate, bool isPartDay, string startTime, string endTime)
    {
        if (!isPartDay)
        {
            return (startDate, endDate.AddDays(1));
        }

        var start = startDate.Add(Set.ParseTime(startTime));
        var end = startDate.Add(Set.ParseTime(endTime));
        return (start, end <= start ? end.AddDays(1) : end);
    }

    /// <summary>Validates <paramref name="input"/> and, when it is fine, copies it onto <paramref name="leave"/>. Returns the errors, keyed by input property.</summary>
    private async Task<Dictionary<string, string>> ApplyAsync(LeaveInput input, EmployeeLeave leave, int? existingId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();

        if (!await _db.Profiles.AnyAsync(p => p.Id == input.EmployeeId, cancellationToken))
        {
            errors[nameof(LeaveInput.EmployeeId)] = "Choose an employee.";
        }

        if (!Enum.TryParse<LeaveType>(input.LeaveType, true, out var type) || !Enum.IsDefined(type))
        {
            errors[nameof(LeaveInput.LeaveType)] = $"The type must be one of: {string.Join(", ", Enum.GetNames<LeaveType>())}.";
        }

        var start = input.StartDate.Date;
        var end = input.EndDate.Date;
        if (end < start)
        {
            errors[nameof(LeaveInput.EndDate)] = "The last day is before the first day.";
        }
        else if ((end - start).TotalDays + 1 > MaxDays)
        {
            errors[nameof(LeaveInput.EndDate)] = $"A leave can be at most {MaxDays} days.";
        }

        var startTime = string.Empty;
        var endTime = string.Empty;
        if (input.IsPartDay)
        {
            if (end != start)
            {
                errors[nameof(LeaveInput.EndDate)] = "A leave of part of a day is on one day: the last day must be the first day.";
            }

            if (!Set.IsValidTime(input.StartTime))
            {
                errors[nameof(LeaveInput.StartTime)] = "Give the start as a time, such as 09:30.";
            }

            if (!Set.IsValidTime(input.EndTime))
            {
                errors[nameof(LeaveInput.EndTime)] = "Give the end as a time, such as 12:00.";
            }

            if (!errors.ContainsKey(nameof(LeaveInput.StartTime)) && !errors.ContainsKey(nameof(LeaveInput.EndTime)))
            {
                startTime = Set.ParseTime(input.StartTime!).ToString(@"hh\:mm");
                endTime = Set.ParseTime(input.EndTime!).ToString(@"hh\:mm");
                if (startTime == endTime)
                {
                    errors[nameof(LeaveInput.EndTime)] = "The leave ends when it starts.";
                }
            }
        }

        var notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        if (notes is not null && notes.Length > NotesMaxLength)
        {
            errors[nameof(LeaveInput.Notes)] = $"The notes can be at most {NotesMaxLength} characters.";
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        // Leaves of one employee must not overlap. A part-day leave may run past midnight, so look a day before as well.
        var candidate = Interval(start, end, input.IsPartDay, startTime, endTime);
        var others = await _db.EmployeeLeaves.AsNoTracking()
            .Where(l => l.EmployeeId == input.EmployeeId && l.Id != existingId && l.EndDate >= start.AddDays(-1) && l.StartDate <= end.AddDays(1))
            .ToListAsync(cancellationToken);
        var clash = others.FirstOrDefault(o =>
        {
            var other = Interval(o.StartDate.Date, o.EndDate.Date, o.IsPartDay, o.StartTime, o.EndTime);
            return other.Start < candidate.End && other.End > candidate.Start;
        });
        if (clash is not null)
        {
            errors[nameof(LeaveInput.StartDate)] = $"This overlaps another leave of the same employee ({clash.Title}).";
            return errors;
        }

        leave.EmployeeId = input.EmployeeId;
        leave.LeaveType = type;
        leave.StartDate = start;
        leave.EndDate = end;
        leave.IsPartDay = input.IsPartDay;
        leave.StartTime = startTime;
        leave.EndTime = endTime;
        leave.Notes = notes;
        return errors;
    }
}
