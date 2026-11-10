using robeem.Application.HR.Seeding;
using robeem.DataModel;

namespace robeem.Tests.HR;

public sealed class HolidaySeedContributorTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ANewTenant_GetsThe2026Calendar_WithTheLunarDatesProvisional_AndARerunAddsNothing()
    {
        await SeedAsync(TestDatabase.TenantA);
        await SeedAsync(TestDatabase.TenantA);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var holidays = db.PublicHolidays.OrderBy(h => h.Date).ToList();
        Assert.Equal(HolidaySeedContributor.Holidays2026().Count, holidays.Count);
        Assert.All(holidays, h => Assert.Equal(2026, h.Date.Year));
        Assert.Equal(holidays.Count, holidays.Select(h => h.Date).Distinct().Count());

        Assert.Equal((false, "Labour Day"), (holidays.Single(h => h.Date == new DateTime(2026, 5, 1)).IsProvisional, holidays.Single(h => h.Date == new DateTime(2026, 5, 1)).Title));
        Assert.Equal(["Eid al-Fitr", "Eid al-Fitr", "Eid al-Fitr"], holidays.Where(h => h.Date >= new DateTime(2026, 3, 20) && h.Date <= new DateTime(2026, 3, 22)).Select(h => h.Title));
        Assert.All(holidays.Where(h => h.Title.StartsWith("Eid", StringComparison.Ordinal) || h.Title is "Ashura" or "Islamic New Year"), h => Assert.True(h.IsProvisional));
        Assert.All(holidays.Where(h => h.Title is "Christmas Day" or "Independence Day" or "Labour Day"), h => Assert.False(h.IsProvisional));
    }

    [Fact]
    public async Task ATenantThatHasAHoliday_IsLeftAlone_SoARemovedHolidayIsNeverAddedBack()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.PublicHolidays.Add(new PublicHoliday { Date = new DateTime(2027, 1, 1), Title = "Own" });
            await db.SaveChangesAsync();
        }

        await SeedAsync(TestDatabase.TenantA);

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal("Own", Assert.Single(check.PublicHolidays).Title);
    }

    [Fact]
    public async Task EachTenant_IsSeededOnItsOwn()
    {
        await SeedAsync(TestDatabase.TenantA);
        await SeedAsync(TestDatabase.TenantB);

        using var a = _database.CreateContext(TestDatabase.TenantA);
        using var b = _database.CreateContext(TestDatabase.TenantB);
        Assert.Equal(a.PublicHolidays.Count(), b.PublicHolidays.Count());
        Assert.NotEqual(0, a.PublicHolidays.Count());
    }

    private async Task SeedAsync(string tenantId)
    {
        using var db = _database.CreateContext(tenantId);
        await new HolidaySeedContributor().SeedAsync(db, CancellationToken.None);
        await db.SaveChangesAsync();
    }
}
