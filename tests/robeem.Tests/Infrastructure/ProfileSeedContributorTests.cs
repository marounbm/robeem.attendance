using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.HR.Seeding;
using robeem.Application.Seeding;
using robeem.DataModel;
using robeem.Infrastructure.Seeding;

namespace robeem.Tests.Infrastructure;

/// <summary>The sample employees of the time-clock export: who gets them, and that a run never disturbs anyone.</summary>
public sealed class ProfileSeedContributorTests : IDisposable
{
    private static readonly string[] Numbers = ["21001", "22001", "22002", "43001"];

    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ANamedTenant_GetsTheFourEmployeesOfTheExport_OnTheScheduleWithTheirDepartment_AndRerunsChangeNothing()
    {
        await SeedAsync(TestDatabase.TenantA, [TestDatabase.TenantA]);
        await SeedAsync(TestDatabase.TenantA, [TestDatabase.TenantA]);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var profiles = db.Profiles.OrderBy(p => p.EmployeeNumber).ToList();
        Assert.Equal(Numbers, profiles.Select(p => p.EmployeeNumber));

        var charbel = profiles.Single(p => p.EmployeeNumber == "22001");
        Assert.Equal(("Charbel", "El Khoury", "Charbel El Khoury", "2 - Sales", true), (charbel.FirstName, charbel.LastName, charbel.Title, charbel.Department, charbel.IsActive));
        Assert.Equal(ProfileSeedContributor.HireDate, charbel.HireDate);
        Assert.Null(charbel.Email);

        // Every employee is on the schedule, in its default team (CLAUDE.md §3.2), once.
        var team = db.TimeScheduleTeams.Single(t => t.AllInclusive);
        var assignments = db.TimeScheduleEmployees.ToList();
        Assert.Equal(profiles.Select(p => p.Id).Order(), assignments.Select(a => a.EmployeeId).Order());
        Assert.All(assignments, a => Assert.Equal((team.TimeScheduleId, team.Id), (a.TimeScheduleId, a.TeamId)));
    }

    [Fact]
    public async Task ATenantNotNamed_GetsNobody()
    {
        await SeedAsync(TestDatabase.TenantA, []);
        await SeedAsync(TestDatabase.TenantB, [TestDatabase.TenantA]);

        using var a = _database.CreateContext(TestDatabase.TenantA);
        using var b = _database.CreateContext(TestDatabase.TenantB);
        Assert.Empty(a.Profiles);
        Assert.Empty(b.Profiles);
    }

    [Fact]
    public async Task EmployeesAlreadyThere_AreNeverTouched_AndOnlyTheMissingOnesAreAdded()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            // The tenant already has someone, and one of the four with the administrator's own edits.
            db.Profiles.AddRange(
                new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" },
                new Profile { EmployeeNumber = "22001", FirstName = "Charbel", LastName = "Khoury-Edited", Title = "Charbel Khoury-Edited", IsActive = false });
            db.SaveChanges();
        }

        await SeedAsync(TestDatabase.TenantA, [TestDatabase.TenantA]);

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(["21001", "22001", "22002", "43001", "E-1"], check.Profiles.Select(p => p.EmployeeNumber).OrderBy(n => n));
        var edited = check.Profiles.Single(p => p.EmployeeNumber == "22001");
        Assert.Equal(("Khoury-Edited", false), (edited.LastName, edited.IsActive));

        // Neither the existing two nor anyone already on a schedule gets an assignment: only the three new ones.
        Assert.Equal(["21001", "22002", "43001"], check.TimeScheduleEmployees.Include(a => a.Employee).Select(a => a.Employee!.EmployeeNumber).OrderBy(n => n));
    }

    [Fact]
    public async Task WithoutAnySchedule_TheEmployeesAreStillCreated()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            await new SeedDataRunner(db, [Contributor([TestDatabase.TenantA])], NullLogger<SeedDataRunner>.Instance).RunAsync(CancellationToken.None);
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(4, check.Profiles.Count());
        Assert.Empty(check.TimeScheduleEmployees);
    }

    private static ProfileSeedContributor Contributor(string[] tenants) =>
        new(Options.Create(new SampleDataOptions { TenantIds = tenants }));

    private async Task SeedAsync(string tenant, string[] sampleTenants)
    {
        using var db = _database.CreateContext(tenant);
        await new SeedDataRunner(db, new ISeedContributor[] { new HrSeedContributor(), Contributor(sampleTenants) }, NullLogger<SeedDataRunner>.Instance)
            .RunAsync(CancellationToken.None);
    }
}
