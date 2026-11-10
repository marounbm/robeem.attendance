using Microsoft.EntityFrameworkCore;
using robeem.Application.HR.Holidays;
using robeem.Contracts.HR.Holidays;
using robeem.DataModel;

namespace robeem.Tests.HR;

public sealed class HolidayServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ADayHoliday_IsAddedWithItsNameAndFlag_AndListedByDate()
    {
        await CreateAsync(new DateTime(2026, 5, 1), "Labour Day");
        await CreateAsync(new DateTime(2026, 1, 1), "  New Year's Day ", provisional: true);

        var list = await ListAsync(2026);

        Assert.Equal([new DateTime(2026, 1, 1), new DateTime(2026, 5, 1)], list.Select(h => h.Date));
        Assert.Equal(["New Year's Day", "Labour Day"], list.Select(h => h.Name));
        Assert.Equal([true, false], list.Select(h => h.IsProvisional));
    }

    [Fact]
    public async Task AHolidayOfSeveralDays_IsOneRowPerDay()
    {
        var result = await CreateAsync(new DateTime(2026, 3, 20), "Eid al-Fitr", lastDate: new DateTime(2026, 3, 22));

        Assert.True(result.Succeeded);
        var list = await ListAsync(2026);
        Assert.Equal([new DateTime(2026, 3, 20), new DateTime(2026, 3, 21), new DateTime(2026, 3, 22)], list.Select(h => h.Date));
        Assert.All(list, h => Assert.Equal("Eid al-Fitr", h.Name));
    }

    [Fact]
    public async Task ADateThatAlreadyHasAHoliday_IsRefused_AndNothingIsAdded()
    {
        await CreateAsync(new DateTime(2026, 3, 21), "Mother's Day");

        var clash = await CreateAsync(new DateTime(2026, 3, 20), "Eid al-Fitr", lastDate: new DateTime(2026, 3, 22));

        Assert.Contains(nameof(HolidayInput.Date), clash.Errors.Keys);
        Assert.Contains("Mother's Day", clash.Errors[nameof(HolidayInput.Date)]);
        Assert.Single(await ListAsync(2026));
    }

    [Fact]
    public async Task BadInput_IsRefusedWithTheFieldNamed()
    {
        Assert.Contains(nameof(HolidayInput.Name), (await CreateAsync(new DateTime(2026, 1, 1), "   ")).Errors.Keys);
        Assert.Contains(nameof(HolidayInput.Name), (await CreateAsync(new DateTime(2026, 1, 1), new string('x', 256))).Errors.Keys);
        Assert.Contains(nameof(HolidayInput.LastDate), (await CreateAsync(new DateTime(2026, 1, 5), "Back to front", lastDate: new DateTime(2026, 1, 4))).Errors.Keys);
        Assert.Contains(nameof(HolidayInput.LastDate), (await CreateAsync(new DateTime(2026, 1, 1), "Too long", lastDate: new DateTime(2026, 3, 1))).Errors.Keys);
        Assert.Empty(await ListAsync(2026));
    }

    [Fact]
    public async Task AHoliday_CanBeEdited_ButNotOntoAnotherHolidaysDate()
    {
        var first = (await CreateAsync(new DateTime(2026, 6, 16), "Islamic New Year", provisional: true)).Id!.Value;
        await CreateAsync(new DateTime(2026, 6, 17), "Other");

        var clash = await UpdateAsync(first, new DateTime(2026, 6, 17), "Islamic New Year", provisional: false);
        Assert.Contains(nameof(HolidayInput.Date), clash.Errors.Keys);

        var moved = await UpdateAsync(first, new DateTime(2026, 6, 18), "Islamic New Year", provisional: false);
        Assert.True(moved.Succeeded);
        var item = (await Service().GetAsync(first, CancellationToken.None))!;
        Assert.Equal((new DateTime(2026, 6, 18), false), (item.Date, item.IsProvisional));

        Assert.True((await UpdateAsync(first, new DateTime(2026, 6, 18), "Same date, new name", provisional: false)).Succeeded);
        Assert.True((await Service().UpdateAsync(9999, new HolidayInput { Date = DateTime.Today, Name = "x" }, CancellationToken.None)).NotFound);
    }

    [Fact]
    public async Task AHoliday_CanBeRemoved_AndOnlyOnce()
    {
        var id = (await CreateAsync(new DateTime(2026, 5, 1), "Labour Day")).Id!.Value;

        Assert.True((await Service().DeleteAsync(id, CancellationToken.None)).Succeeded);
        Assert.True((await Service().DeleteAsync(id, CancellationToken.None)).NotFound);
        Assert.Empty(await ListAsync(2026));
    }

    [Fact]
    public async Task TheYears_AreThoseThatHaveAHoliday_LatestFirst()
    {
        await CreateAsync(new DateTime(2026, 5, 1), "A");
        await CreateAsync(new DateTime(2025, 5, 1), "B");
        await CreateAsync(new DateTime(2026, 6, 1), "C");

        Assert.Equal([2026, 2025], await Service().ListYearsAsync(CancellationToken.None));
        Assert.Single(await ListAsync(2025));
    }

    [Fact]
    public async Task AnotherTenantsHolidays_AreNotSeen()
    {
        await CreateAsync(new DateTime(2026, 5, 1), "Labour Day");

        using var other = _database.CreateContext(TestDatabase.TenantB);
        Assert.Empty(await new HolidayService(other).ListAsync(2026, CancellationToken.None));
        Assert.True((await new HolidayService(other).CreateAsync(new HolidayInput { Date = new DateTime(2026, 5, 1), Name = "Own" }, CancellationToken.None)).Succeeded);
    }

    private HolidayService Service() => new(_database.CreateContext(TestDatabase.TenantA));

    private Task<IReadOnlyList<HolidayItem>> ListAsync(int year) => Service().ListAsync(year, CancellationToken.None);

    private Task<robeem.Application.Common.SaveResult> CreateAsync(DateTime date, string name, bool provisional = false, DateTime? lastDate = null) =>
        Service().CreateAsync(new HolidayInput { Date = date, LastDate = lastDate, Name = name, IsProvisional = provisional }, CancellationToken.None);

    private Task<robeem.Application.Common.SaveResult> UpdateAsync(int id, DateTime date, string name, bool provisional) =>
        Service().UpdateAsync(id, new HolidayInput { Date = date, Name = name, IsProvisional = provisional }, CancellationToken.None);
}
