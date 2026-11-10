using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.HR.Holidays;
using robeem.DataModel;

namespace robeem.Application.HR.Holidays;

/// <summary>
/// Public holidays of the tenant: one row per day. A holiday list is small by nature and bounded by its year,
/// so it is not paged (CLAUDE.md §2.9).
/// </summary>
public sealed class HolidayService : IHolidayService
{
    private const int MaxDays = 31;

    private readonly IRobeemDbContext _db;

    public HolidayService(IRobeemDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<HolidayItem>> ListAsync(int year, CancellationToken cancellationToken)
    {
        var first = new DateTime(year, 1, 1);
        var next = first.AddYears(1);
        var rows = await _db.PublicHolidays
            .AsNoTracking()
            .Where(h => h.Date >= first && h.Date < next)
            .OrderBy(h => h.Date)
            .ThenBy(h => h.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(ToItem).ToList();
    }

    public async Task<IReadOnlyList<int>> ListYearsAsync(CancellationToken cancellationToken)
    {
        var dates = await _db.PublicHolidays.AsNoTracking().Select(h => h.Date).ToListAsync(cancellationToken);
        return dates.Select(d => d.Year).Distinct().OrderByDescending(y => y).ToList();
    }

    public async Task<HolidayItem?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var row = await _db.PublicHolidays.AsNoTracking().SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        return row is null ? null : ToItem(row);
    }

    public async Task<SaveResult> CreateAsync(HolidayInput input, CancellationToken cancellationToken)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        var first = input.Date.Date;
        var last = (input.LastDate ?? input.Date).Date;

        var errors = ValidateName(name);
        if (last < first)
        {
            errors[nameof(HolidayInput.LastDate)] = "The last day is before the first day.";
        }
        else if ((last - first).TotalDays + 1 > MaxDays)
        {
            errors[nameof(HolidayInput.LastDate)] = $"A holiday can run at most {MaxDays} days.";
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var dates = Enumerable.Range(0, (int)(last - first).TotalDays + 1).Select(i => first.AddDays(i)).ToList();
        var taken = await _db.PublicHolidays.AsNoTracking()
            .Where(h => dates.Contains(h.Date))
            .Select(h => new { h.Date, h.Title })
            .FirstOrDefaultAsync(cancellationToken);
        if (taken is not null)
        {
            return SaveResult.Invalid(nameof(HolidayInput.Date), $"{taken.Date:yyyy-MM-dd} already has a holiday ({taken.Title}).");
        }

        var rows = dates.Select(d => new PublicHoliday { Date = d, Title = name, IsProvisional = input.IsProvisional }).ToList();
        _db.PublicHolidays.AddRange(rows);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent save of the same date (unique index).
            _db.ChangeTracker.Clear();
            return SaveResult.Invalid(nameof(HolidayInput.Date), "A holiday already exists on one of those dates.");
        }

        return SaveResult.Success(rows[0].Id);
    }

    public async Task<SaveResult> UpdateAsync(int id, HolidayInput input, CancellationToken cancellationToken)
    {
        var row = await _db.PublicHolidays.SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (row is null)
        {
            return SaveResult.Missing();
        }

        var name = input.Name?.Trim() ?? string.Empty;
        var errors = ValidateName(name);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var date = input.Date.Date;
        var other = await _db.PublicHolidays.AsNoTracking()
            .Where(h => h.Date == date && h.Id != id)
            .Select(h => h.Title)
            .FirstOrDefaultAsync(cancellationToken);
        if (other is not null)
        {
            return SaveResult.Invalid(nameof(HolidayInput.Date), $"{date:yyyy-MM-dd} already has a holiday ({other}).");
        }

        row.Date = date;
        row.Title = name;
        row.IsProvisional = input.IsProvisional;
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(id);
    }

    public async Task<SaveResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var row = await _db.PublicHolidays.SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (row is null)
        {
            return SaveResult.Missing();
        }

        _db.PublicHolidays.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(id);
    }

    private static Dictionary<string, string> ValidateName(string name)
    {
        var errors = new Dictionary<string, string>();
        if (name.Length == 0)
        {
            errors[nameof(HolidayInput.Name)] = "Give the holiday a name.";
        }
        else if (name.Length > 255)
        {
            errors[nameof(HolidayInput.Name)] = "The name can be at most 255 characters.";
        }

        return errors;
    }

    private static HolidayItem ToItem(PublicHoliday h) =>
        new() { Id = h.Id, Date = h.Date.Date, Name = h.Title, IsProvisional = h.IsProvisional };
}
