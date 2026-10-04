using Microsoft.EntityFrameworkCore;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;
using robeem.Infrastructure.Tenancy;

namespace robeem.Tests.Infrastructure;

public sealed class TenantIsolationTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public void Insert_StampsTenant_OnClrAndShadowProperties()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.Profiles.Add(NewProfile("E-1"));
            db.TimeSchedules.Add(new TimeSchedule { Title = "Week" });
            db.SaveChanges();
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(TestDatabase.TenantA, check.Profiles.Single().TenantId);

        var schedule = check.TimeSchedules.Single();
        Assert.Equal(TestDatabase.TenantA, check.Entry(schedule).Property(RobeemDbContext.TenantIdProperty).CurrentValue);
    }

    [Fact]
    public void Queries_OnlyReturnTheCurrentTenantsRows()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.Profiles.Add(NewProfile("E-1"));
            db.TimeSchedules.Add(new TimeSchedule { Title = "Week" });
            db.SaveChanges();
        }

        using var other = _database.CreateContext(TestDatabase.TenantB);
        Assert.Empty(other.Profiles);
        Assert.Empty(other.TimeSchedules);
        Assert.Equal(1, other.Profiles.IgnoreQueryFilters().Count());
    }

    [Fact]
    public void Insert_ForAnotherTenant_Throws()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var profile = NewProfile("E-1");
        profile.TenantId = TestDatabase.TenantB;
        db.Profiles.Add(profile);

        Assert.Throws<TenantIsolationException>(() => db.SaveChanges());
    }

    [Fact]
    public void Update_OfAnotherTenantsRow_Throws()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.Profiles.Add(NewProfile("E-1"));
            db.SaveChanges();
        }

        using var other = _database.CreateContext(TestDatabase.TenantB);
        var stolen = other.Profiles.IgnoreQueryFilters().Single();
        stolen.FirstName = "Changed";

        Assert.Throws<TenantIsolationException>(() => other.SaveChanges());
    }

    [Fact]
    public void Update_ThatMovesARowToAnotherTenant_Throws()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        db.Profiles.Add(NewProfile("E-1"));
        db.SaveChanges();

        db.Profiles.Single().TenantId = TestDatabase.TenantB;

        Assert.Throws<TenantIsolationException>(() => db.SaveChanges());
    }

    [Fact]
    public void DataAccess_WithoutTenant_FailsClosed()
    {
        using var db = _database.CreateContext(null);

        Assert.Throws<TenantNotResolvedException>(() => db.Profiles.ToList());
    }

    private static Profile NewProfile(string number)
        => new() { EmployeeNumber = number, FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
}
