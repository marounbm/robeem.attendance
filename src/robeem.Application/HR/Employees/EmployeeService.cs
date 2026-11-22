using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Employees;
using robeem.DataModel;

namespace robeem.Application.HR.Employees;

/// <summary>
/// Employee (<see cref="Profile"/>) maintenance, including the employee's time schedule assignment
/// (<see cref="TimeScheduleEmployee"/>). Assigning a schedule places the employee in that
/// schedule's all-inclusive team, which is the schedule's default team containing every
/// employee assigned to the schedule.
/// </summary>
public sealed class EmployeeService : IEmployeeService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

    private readonly IRobeemDbContext _db;

    public EmployeeService(IRobeemDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<EmployeeListItem>> ListAsync(string? search, bool includeInactive, int page, int pageSize, EmployeeSortField sort, bool descending, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);
        var profiles = _db.Profiles.AsNoTracking();

        if (!includeInactive)
        {
            profiles = profiles.Where(p => p.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // PostgreSQL compares text case-sensitively; lower-case both sides so search is not.
            var term = search.Trim().ToLowerInvariant();
            profiles = profiles.Where(p =>
                p.EmployeeNumber.ToLower().Contains(term)
                || p.FirstName.ToLower().Contains(term)
                || p.LastName.ToLower().Contains(term)
                || (p.Email != null && p.Email.ToLower().Contains(term))
                || (p.Department != null && p.Department.ToLower().Contains(term)));
        }

        // The total is counted with the same filters as the page; a page past the end is answered with the last page.
        var total = await profiles.CountAsync(cancellationToken);
        var current = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)size)));

        var rows =
            from p in profiles
            join a in _db.TimeScheduleEmployees on p.Id equals a.EmployeeId into assignments
            from a in assignments.DefaultIfEmpty()
            join s in _db.TimeSchedules on a.TimeScheduleId equals s.Id into schedules
            from s in schedules.DefaultIfEmpty()
            join m in _db.Profiles on p.ManagerId equals m.Id into managers
            from m in managers.DefaultIfEmpty()
            select new Row { P = p, S = s, M = m };

        // The employee number is unique per tenant and ends every order, so the order is total and pages never overlap.
        var query = Sorted(rows, sort, descending)
            .Skip((current - 1) * size)
            .Take(size)
            .Select(x => new EmployeeListItem
            {
                Id = x.P.Id,
                EmployeeNumber = x.P.EmployeeNumber,
                FullName = x.P.FirstName + " " + x.P.LastName,
                Department = x.P.Department,
                JobTitle = x.P.JobTitle,
                HireDate = x.P.HireDate,
                IsActive = x.P.IsActive,
                PayBasis = x.P.PayBasis.ToString(),
                TimeScheduleTitle = x.S != null ? x.S.Title : null,
                ManagerName = x.M != null ? x.M.FirstName + " " + x.M.LastName : null,
            });

        return new PagedResult<EmployeeListItem> { Items = await query.ToListAsync(cancellationToken), Total = total, Page = current, PageSize = size };
    }

    public async Task<EmployeeInput?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var profile = await _db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var scheduleId = await _db.TimeScheduleEmployees
            .Where(a => a.EmployeeId == id)
            .Select(a => (int?)a.TimeScheduleId)
            .SingleOrDefaultAsync(cancellationToken);

        return new EmployeeInput
        {
            EmployeeNumber = profile.EmployeeNumber,
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Email = profile.Email,
            Phone = profile.Phone,
            Department = profile.Department,
            JobTitle = profile.JobTitle,
            HireDate = profile.HireDate,
            TerminationDate = profile.TerminationDate,
            IsActive = profile.IsActive,
            PayBasis = profile.PayBasis.ToString(),
            ManagerId = profile.ManagerId,
            TimeScheduleId = scheduleId,
        };
    }

    public async Task<IReadOnlyList<ManagerOption>> ListManagerOptionsAsync(int? excludeEmployeeId, CancellationToken cancellationToken)
    {
        var rows = await _db.Profiles
            .AsNoTracking()
            .Where(p => p.IsActive && p.Id != excludeEmployeeId)
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ThenBy(p => p.EmployeeNumber)
            .Select(p => new { p.Id, p.FirstName, p.LastName, p.EmployeeNumber })
            .ToListAsync(cancellationToken);

        return rows.Select(p => new ManagerOption { Id = p.Id, Name = $"{p.FirstName} {p.LastName} ({p.EmployeeNumber})" }).ToList();
    }

    public async Task<IReadOnlyList<EmployeeLookupItem>> LookupAsync(string? search, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return [];
        }

        // PostgreSQL compares text case-sensitively; lower-case both sides so search is not.
        var term = search.Trim().ToLowerInvariant();
        var rows = await _db.Profiles
            .AsNoTracking()
            .Where(p => p.EmployeeNumber.ToLower().Contains(term) || p.FirstName.ToLower().Contains(term) || p.LastName.ToLower().Contains(term))
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ThenBy(p => p.EmployeeNumber)
            .Take(20)
            .Select(p => new { p.Id, p.EmployeeNumber, p.FirstName, p.LastName })
            .ToListAsync(cancellationToken);

        return rows.Select(p => new EmployeeLookupItem { Id = p.Id, EmployeeNumber = p.EmployeeNumber, Name = $"{p.FirstName} {p.LastName}" }).ToList();
    }

    public async Task<IReadOnlyList<ScheduleOption>> ListSchedulesAsync(CancellationToken cancellationToken)
        => await _db.TimeSchedules
            .AsNoTracking()
            .OrderBy(s => s.Title)
            .Select(s => new ScheduleOption { Id = s.Id, Title = s.Title, IsOnDemand = _db.OnDemandTimeSchedules.Any(o => o.TimeScheduleId == s.Id) })
            .ToListAsync(cancellationToken);

    public async Task<EmployeeSaveResult> CreateAsync(EmployeeInput input, CancellationToken cancellationToken)
    {
        var errors = await ValidateAsync(input, null, cancellationToken);
        if (errors.Count > 0)
        {
            return EmployeeSaveResult.Invalid(errors);
        }

        var profile = new Profile();
        Apply(input, profile);
        _db.Profiles.Add(profile);

        var scheduleError = await AssignScheduleAsync(profile, null, input.TimeScheduleId, cancellationToken);
        if (scheduleError is not null)
        {
            _db.ChangeTracker.Clear();
            return EmployeeSaveResult.Invalid(new Dictionary<string, string> { [nameof(EmployeeInput.TimeScheduleId)] = scheduleError });
        }

        return await SaveAsync(profile, input, cancellationToken);
    }

    public async Task<EmployeeSaveResult> UpdateAsync(int id, EmployeeInput input, CancellationToken cancellationToken)
    {
        var profile = await _db.Profiles.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (profile is null)
        {
            return EmployeeSaveResult.Missing();
        }

        var errors = await ValidateAsync(input, id, cancellationToken);
        if (errors.Count > 0)
        {
            return EmployeeSaveResult.Invalid(errors);
        }

        Apply(input, profile);

        var assignment = await _db.TimeScheduleEmployees.SingleOrDefaultAsync(a => a.EmployeeId == id, cancellationToken);
        var scheduleError = await AssignScheduleAsync(profile, assignment, input.TimeScheduleId, cancellationToken);
        if (scheduleError is not null)
        {
            _db.ChangeTracker.Clear();
            return EmployeeSaveResult.Invalid(new Dictionary<string, string> { [nameof(EmployeeInput.TimeScheduleId)] = scheduleError });
        }

        return await SaveAsync(profile, input, cancellationToken);
    }

    /// <summary>
    /// Orders the rows by a column. A column that can be empty (department, job title, schedule, manager) puts the employees that have nothing last,
    /// ascending or descending, on every database, and the employee number, in natural order, breaks every tie.
    /// </summary>
    private static IOrderedQueryable<Row> Sorted(IQueryable<Row> rows, EmployeeSortField sort, bool descending)
    {
        IOrderedQueryable<Row> By<TKey>(Expression<Func<Row, TKey>> key) =>
            descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

        IOrderedQueryable<Row> ByOptional<TKey>(Expression<Func<Row, bool>> isEmpty, Expression<Func<Row, TKey>> key) =>
            descending ? rows.OrderBy(isEmpty).ThenByDescending(key) : rows.OrderBy(isEmpty).ThenBy(key);

        return sort switch
        {
            EmployeeSortField.Name => (descending ? rows.OrderByDescending(r => r.P.LastName).ThenByDescending(r => r.P.FirstName) : rows.OrderBy(r => r.P.LastName).ThenBy(r => r.P.FirstName)).ThenBy(r => r.P.EmployeeNumber.Length).ThenBy(r => r.P.EmployeeNumber),
            EmployeeSortField.Department => ByOptional(r => r.P.Department == null, r => r.P.Department).ThenBy(r => r.P.EmployeeNumber.Length).ThenBy(r => r.P.EmployeeNumber),
            EmployeeSortField.JobTitle => ByOptional(r => r.P.JobTitle == null, r => r.P.JobTitle).ThenBy(r => r.P.EmployeeNumber.Length).ThenBy(r => r.P.EmployeeNumber),
            EmployeeSortField.TimeSchedule => ByOptional(r => r.S == null, r => r.S!.Title).ThenBy(r => r.P.EmployeeNumber.Length).ThenBy(r => r.P.EmployeeNumber),
            EmployeeSortField.Manager => ByOptional(r => r.M == null, r => r.M!.FirstName + " " + r.M.LastName).ThenBy(r => r.P.EmployeeNumber.Length).ThenBy(r => r.P.EmployeeNumber),
            EmployeeSortField.HireDate => By(r => r.P.HireDate).ThenBy(r => r.P.EmployeeNumber.Length).ThenBy(r => r.P.EmployeeNumber),

            // Ascending lists the active employees first.
            EmployeeSortField.Status => (descending ? rows.OrderBy(r => r.P.IsActive) : rows.OrderByDescending(r => r.P.IsActive)).ThenBy(r => r.P.EmployeeNumber.Length).ThenBy(r => r.P.EmployeeNumber),
            _ => descending
                ? rows.OrderByDescending(r => r.P.EmployeeNumber.Length).ThenByDescending(r => r.P.EmployeeNumber)
                : rows.OrderBy(r => r.P.EmployeeNumber.Length).ThenBy(r => r.P.EmployeeNumber),
        };
    }

    private static void Apply(EmployeeInput input, Profile profile)
    {
        profile.EmployeeNumber = input.EmployeeNumber.Trim();
        profile.FirstName = input.FirstName.Trim();
        profile.LastName = input.LastName.Trim();
        profile.Email = NullIfBlank(input.Email);
        profile.Phone = NullIfBlank(input.Phone);
        profile.Department = NullIfBlank(input.Department);
        profile.JobTitle = NullIfBlank(input.JobTitle);
        profile.HireDate = input.HireDate.Date;
        profile.TerminationDate = input.TerminationDate?.Date;
        profile.IsActive = input.IsActive;
        profile.PayBasis = Enum.TryParse<PayBasis>(input.PayBasis, out var payBasis) ? payBasis : PayBasis.Scheduled;
        profile.ManagerId = input.ManagerId;
        profile.Title = profile.FullName;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<Dictionary<string, string>> ValidateAsync(EmployeeInput input, int? existingId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();

        if (input.TerminationDate is { } terminated && terminated.Date < input.HireDate.Date)
        {
            errors[nameof(EmployeeInput.TerminationDate)] = "Termination date cannot be before the hire date.";
        }

        if (!EmployeeInput.PayBases.Contains(input.PayBasis, StringComparer.Ordinal))
        {
            errors[nameof(EmployeeInput.PayBasis)] = $"Choose one of: {string.Join(", ", EmployeeInput.PayBases)}.";
        }

        if (await IsEmployeeNumberTakenAsync(input.EmployeeNumber, existingId, cancellationToken))
        {
            errors[nameof(EmployeeInput.EmployeeNumber)] = "Another employee already has this employee number.";
        }

        if (input.ManagerId is { } managerId && await ManagerProblemAsync(managerId, existingId, cancellationToken) is { } problem)
        {
            errors[nameof(EmployeeInput.ManagerId)] = problem;
        }

        return errors;
    }

    /// <summary>Why <paramref name="managerId"/> can't be this employee's manager, or null when they can.</summary>
    private async Task<string?> ManagerProblemAsync(int managerId, int? employeeId, CancellationToken cancellationToken)
    {
        if (managerId == employeeId)
        {
            return "An employee cannot be their own manager.";
        }

        var chain = await _db.Profiles
            .AsNoTracking()
            .Select(p => new { p.Id, p.ManagerId, p.IsActive })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        if (!chain.TryGetValue(managerId, out var manager))
        {
            return "The selected manager does not exist.";
        }

        if (!manager.IsActive)
        {
            return "The selected manager is not active.";
        }

        // Walking up from the manager must never reach the employee: that would make a loop.
        var seen = new HashSet<int>();
        for (int? next = manager.ManagerId; next is { } id && employeeId is not null && seen.Add(id); next = chain.GetValueOrDefault(id)?.ManagerId)
        {
            if (id == employeeId)
            {
                return "That would make a loop: the selected manager already reports to this employee.";
            }
        }

        return null;
    }

    private Task<bool> IsEmployeeNumberTakenAsync(string employeeNumber, int? existingId, CancellationToken cancellationToken)
    {
        var number = employeeNumber.Trim();
        return _db.Profiles.AnyAsync(p => p.EmployeeNumber == number && p.Id != existingId, cancellationToken);
    }

    /// <summary>
    /// Points the employee at <paramref name="timeScheduleId"/>. Staying on the same schedule keeps
    /// the current team; moving to another schedule places the employee in its all-inclusive team.
    /// Returns an error message, or <c>null</c> on success.
    /// </summary>
    private async Task<string?> AssignScheduleAsync(Profile profile, TimeScheduleEmployee? assignment, int? timeScheduleId, CancellationToken cancellationToken)
    {
        if (timeScheduleId is null)
        {
            if (assignment is not null)
            {
                _db.TimeScheduleEmployees.Remove(assignment);
            }

            return null;
        }

        // An on-demand schedule is for people paid by the hour. Checked even when the employee stays on it, so that
        // changing them to a schedule-paid employee can't leave them on a schedule with no shifts.
        if (profile.PayBasis != PayBasis.Hourly && await _db.OnDemandTimeSchedules.AnyAsync(o => o.TimeScheduleId == timeScheduleId, cancellationToken))
        {
            return "This schedule is on demand: only employees paid by the hour can be on it. Choose another schedule, or pay this employee by the hour.";
        }

        if (assignment?.TimeScheduleId == timeScheduleId)
        {
            return null;
        }

        if (!await _db.TimeSchedules.AnyAsync(s => s.Id == timeScheduleId, cancellationToken))
        {
            return "The selected time schedule does not exist.";
        }

        var defaultTeamId = await _db.TimeScheduleTeams
            .Where(t => t.TimeScheduleId == timeScheduleId && t.AllInclusive)
            .OrderBy(t => t.Id)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultTeamId is null)
        {
            return "The selected time schedule has no all-inclusive (default) team.";
        }

        if (assignment is null)
        {
            _db.TimeScheduleEmployees.Add(new TimeScheduleEmployee
            {
                TimeScheduleId = timeScheduleId.Value,
                TeamId = defaultTeamId.Value,
                Employee = profile,
            });
        }
        else
        {
            assignment.TimeScheduleId = timeScheduleId.Value;
            assignment.TeamId = defaultTeamId.Value;
        }

        return null;
    }

    private async Task<EmployeeSaveResult> SaveAsync(Profile profile, EmployeeInput input, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return EmployeeSaveResult.Success(profile.Id);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent save of the same employee number (unique index)?
            _db.ChangeTracker.Clear();
            if (!await IsEmployeeNumberTakenAsync(input.EmployeeNumber, profile.Id == 0 ? null : profile.Id, cancellationToken))
            {
                throw;
            }

            return EmployeeSaveResult.Invalid(new Dictionary<string, string>
            {
                [nameof(EmployeeInput.EmployeeNumber)] = "Another employee already has this employee number.",
            });
        }
    }

    /// <summary>An employee with their schedule and manager, which is what the list sorts and shows. Built with an initializer, which a query can sort through.</summary>
    private sealed class Row
    {
        public Profile P { get; init; } = null!;

        public TimeSchedule? S { get; init; }

        public Profile? M { get; init; }
    }
}
