using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class PayCycleService : IPayCycleService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;

    public PayCycleService(IRobeemDbContext db, IAttendanceAuditTrail audit, TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<PayCycleInput?> GetDefaultAsync(CancellationToken cancellationToken)
    {
        var cycle = await _db.PayCycles.AsNoTracking().SingleOrDefaultAsync(c => c.EmployeeId == null, cancellationToken);
        return cycle is null ? null : ToInput(cycle);
    }

    public async Task<PayCycleItem> DescribeDefaultAsync(CancellationToken cancellationToken) =>
        Describe(await _db.PayCycles.AsNoTracking().SingleOrDefaultAsync(c => c.EmployeeId == null, cancellationToken));

    public async Task<SaveResult> SaveDefaultAsync(PayCycleInput input, CancellationToken cancellationToken)
    {
        if (Validate(input) is { Count: > 0 } errors)
        {
            return SaveResult.Invalid(errors);
        }

        var cycle = await _db.PayCycles.SingleOrDefaultAsync(c => c.EmployeeId == null, cancellationToken);
        var created = cycle is null;
        if (cycle is null)
        {
            cycle = new PayCycle();
            _db.PayCycles.Add(cycle);
        }

        Apply(cycle, input);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AttendanceAuditActions.PayCycleChanged, nameof(PayCycle), cycle.Id, $"{(created ? "Set" : "Changed")} the default pay cycle: {PayCycleCalendar.Describe(cycle)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(cycle.Id);
    }

    public async Task<PagedResult<EmployeePayCycleItem>> ListEmployeesAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);
        var employees = _db.Profiles.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // PostgreSQL compares text case-sensitively; lower-case both sides so search is not.
            var term = search.Trim().ToLowerInvariant();
            employees = employees.Where(p => p.EmployeeNumber.ToLower().Contains(term) || (p.FirstName + " " + p.LastName).ToLower().Contains(term));
        }

        var total = await employees.CountAsync(cancellationToken);
        var current = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)size)));

        // Shorter employee numbers first, so 9 comes before 10; the id breaks every tie, so pages never overlap.
        var rows = await employees
            .OrderBy(p => p.EmployeeNumber.Length)
            .ThenBy(p => p.EmployeeNumber)
            .ThenBy(p => p.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .Select(p => new { p.Id, p.EmployeeNumber, p.FirstName, p.LastName })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var own = await _db.PayCycles.AsNoTracking().Where(c => c.EmployeeId != null && ids.Contains(c.EmployeeId.Value)).ToListAsync(cancellationToken);
        var fallback = await _db.PayCycles.AsNoTracking().SingleOrDefaultAsync(c => c.EmployeeId == null, cancellationToken);

        return new PagedResult<EmployeePayCycleItem>
        {
            Items = rows.Select(r =>
            {
                var mine = own.FirstOrDefault(c => c.EmployeeId == r.Id);
                return new EmployeePayCycleItem
                {
                    EmployeeId = r.Id,
                    EmployeeNumber = r.EmployeeNumber,
                    EmployeeName = $"{r.FirstName} {r.LastName}".Trim(),
                    Source = mine is not null ? "Own" : fallback is not null ? "Default" : "Calendar month",
                    Cycle = Describe(mine ?? fallback),
                };
            }).ToList(),
            Total = total,
            Page = current,
            PageSize = size,
        };
    }

    public async Task<PayCycleInput?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken)
    {
        var cycle = await _db.PayCycles.AsNoTracking().SingleOrDefaultAsync(c => c.EmployeeId == employeeId, cancellationToken);
        return cycle is null ? null : ToInput(cycle);
    }

    public async Task<SaveResult> SaveEmployeeAsync(int employeeId, PayCycleInput input, CancellationToken cancellationToken)
    {
        if (!await _db.Profiles.AnyAsync(p => p.Id == employeeId, cancellationToken))
        {
            return SaveResult.Missing();
        }

        if (Validate(input) is { Count: > 0 } errors)
        {
            return SaveResult.Invalid(errors);
        }

        var cycle = await _db.PayCycles.SingleOrDefaultAsync(c => c.EmployeeId == employeeId, cancellationToken);
        if (cycle is null)
        {
            cycle = new PayCycle { EmployeeId = employeeId };
            _db.PayCycles.Add(cycle);
        }

        Apply(cycle, input);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AttendanceAuditActions.PayCycleChanged, nameof(PayCycle), cycle.Id, $"Set the pay cycle of employee {employeeId}: {PayCycleCalendar.Describe(cycle)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(cycle.Id);
    }

    public async Task<SaveResult> RemoveEmployeeAsync(int employeeId, CancellationToken cancellationToken)
    {
        var cycle = await _db.PayCycles.SingleOrDefaultAsync(c => c.EmployeeId == employeeId, cancellationToken);
        if (cycle is null)
        {
            return SaveResult.Missing();
        }

        _db.PayCycles.Remove(cycle);
        _audit.Record(AttendanceAuditActions.PayCycleChanged, nameof(PayCycle), cycle.Id, $"Removed the pay cycle of employee {employeeId}: the default applies to them again.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(cycle.Id);
    }

    private static Dictionary<string, string> Validate(PayCycleInput input)
    {
        var errors = new Dictionary<string, string>();
        if (!PayCycleInput.Frequencies.Contains(input.Frequency, StringComparer.Ordinal))
        {
            errors[nameof(PayCycleInput.Frequency)] = $"Choose one of: {string.Join(", ", PayCycleInput.Frequencies)}.";
        }

        // Only a monthly or twice-a-month cycle reads the start day, so only then is it checked (the others keep whatever the form posted).
        if (input.Frequency == nameof(PayCycleFrequency.SemiMonthly))
        {
            if (input.StartDay < 1 || input.StartDay > PayCycleCalendar.MaxSemiMonthlyStartDay)
            {
                errors[nameof(PayCycleInput.StartDay)] = $"A cycle twice a month can start on day 1 to {PayCycleCalendar.MaxSemiMonthlyStartDay}: its second period starts 15 days later, and every month has that day.";
            }
        }
        else if (input.Frequency == nameof(PayCycleFrequency.Monthly) && (input.StartDay < 1 || input.StartDay > PayCycleCalendar.MaxStartDay))
        {
            errors[nameof(PayCycleInput.StartDay)] = $"The month can start on day 1 to {PayCycleCalendar.MaxStartDay}, so that every month has it.";
        }

        return errors;
    }

    private static void Apply(PayCycle cycle, PayCycleInput input)
    {
        cycle.Frequency = Enum.Parse<PayCycleFrequency>(input.Frequency);
        cycle.AnchorDate = DateTime.SpecifyKind(input.AnchorDate.Date, DateTimeKind.Unspecified);
        cycle.StartDay = input.StartDay;
    }

    private static PayCycleInput ToInput(PayCycle cycle) => new()
    {
        Frequency = cycle.Frequency.ToString(),
        AnchorDate = cycle.AnchorDate.Date,
        StartDay = cycle.StartDay,
    };

    private PayCycleItem Describe(PayCycle? cycle)
    {
        var today = _clock.GetLocalNow().DateTime.Date;
        var current = PayCycleCalendar.PeriodContaining(cycle, today);
        var last = PayCycleCalendar.LastFinished(cycle, today);
        return new PayCycleItem
        {
            Frequency = (cycle?.Frequency ?? PayCycleFrequency.Monthly).ToString(),
            AnchorDate = cycle?.AnchorDate.Date ?? DateTime.MinValue,
            StartDay = cycle?.StartDay ?? 1,
            Description = PayCycleCalendar.Describe(cycle),
            CurrentStart = current.Start,
            CurrentEnd = current.End,
            LastStart = last.Start,
            LastEnd = last.End,
        };
    }
}
