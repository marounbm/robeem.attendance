using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using robeem.Application.Persistence;
using robeem.Application.Seeding;
using robeem.DataModel;

namespace robeem.Application.HR.Seeding;

/// <summary>
/// Gives the tenants named in <see cref="SampleDataOptions"/> the employees of the sample time-clock
/// export (<c>tests/Attendance data April and May 2026.xls</c>), so its punches have people to
/// belong to. Adds only the employees whose number is missing, so employees an administrator added,
/// edited or deactivated are never touched, and puts each new one on the tenant's first schedule.
/// </summary>
/// <remarks>
/// Only what the export says is recorded: the employee number, the name and the department. The hire
/// date is the first day of the export, because the file does not give one. A tenant not named in
/// the options is left alone, so sample people never reach another tenant's database.
/// </remarks>
public sealed class ProfileSeedContributor : ISeedContributor
{
    /// <summary>The first day of the export: a placeholder hire date.</summary>
    public static readonly DateTime HireDate = new(2026, 4, 1);

    private static readonly SampleEmployee[] Employees =
    [
        new("21001", "Hiba", "Rahme", "1 - Marketing"),
        new("22001", "Charbel", "El Khoury", "2 - Sales"),
        new("22002", "Yara", "Jabbour", "2 - Sales"),
        new("43001", "Anthony", "El Khoury", "3 - Quality Control"),
    ];

    private readonly SampleDataOptions _options;

    public ProfileSeedContributor(IOptions<SampleDataOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Gets the run order; after the HR seed, which creates the schedule.</summary>
    public int Order => 150;

    public async Task SeedAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        if (!_options.TenantIds.Contains(db.CurrentTenantId, StringComparer.Ordinal))
        {
            return;
        }

        var existing = await db.Profiles.Select(p => p.EmployeeNumber).ToListAsync(cancellationToken);
        var missing = Employees.Where(e => !existing.Contains(e.Number, StringComparer.Ordinal)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        // The schedule the HR screens would offer first, with its default team (CLAUDE.md §3.2). Without
        // one the employees are still created: they just have no schedule until HR assigns one.
        var team = await db.TimeScheduleTeams
            .Where(t => t.AllInclusive)
            .OrderBy(t => t.TimeScheduleId)
            .ThenBy(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        foreach (var employee in missing)
        {
            var profile = new Profile
            {
                EmployeeNumber = employee.Number,
                FirstName = employee.FirstName,
                LastName = employee.LastName,
                Title = $"{employee.FirstName} {employee.LastName}",
                Department = employee.Department,
                HireDate = HireDate,
            };
            db.Profiles.Add(profile);

            if (team is not null)
            {
                db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { Employee = profile, TimeScheduleId = team.TimeScheduleId, TeamId = team.Id });
            }
        }
    }

    private sealed record SampleEmployee(string Number, string FirstName, string LastName, string Department);
}
