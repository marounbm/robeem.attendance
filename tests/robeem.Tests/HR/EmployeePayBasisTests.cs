using robeem.Application.HR.Employees;
using robeem.Contracts.HR.Employees;
using robeem.DataModel;

namespace robeem.Tests.HR;

public sealed class EmployeePayBasisTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AnEmployeeIsPaidByTheSchedule_UnlessMarkedHourly_AndItIsStoredAndListed()
    {
        var scheduled = await CreateAsync("E-1", null);
        var hourly = await CreateAsync("E-2", "Hourly");

        Assert.True(scheduled.Succeeded && hourly.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = new EmployeeService(db);
        Assert.Equal("Scheduled", (await service.GetAsync(scheduled.EmployeeId!.Value, CancellationToken.None))!.PayBasis);
        Assert.Equal("Hourly", (await service.GetAsync(hourly.EmployeeId!.Value, CancellationToken.None))!.PayBasis);
        Assert.Equal(PayBasis.Hourly, db.Profiles.Single(p => p.Id == hourly.EmployeeId).PayBasis);
        Assert.Equal(["Scheduled", "Hourly"], (await service.ListAsync(null, false, 1, 20, CancellationToken.None)).Items.OrderBy(i => i.EmployeeNumber).Select(i => i.PayBasis));
    }

    [Fact]
    public async Task ThePayBasis_CanBeChanged_AndAnUnknownOneIsRefused()
    {
        var id = (await CreateAsync("E-1", null)).EmployeeId!.Value;

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var input = (await new EmployeeService(db).GetAsync(id, CancellationToken.None))!;
            input.PayBasis = "Hourly";
            Assert.True((await new EmployeeService(db).UpdateAsync(id, input, CancellationToken.None)).Succeeded);

            input.PayBasis = "Piecework";
            var refused = await new EmployeeService(db).UpdateAsync(id, input, CancellationToken.None);
            Assert.Contains(nameof(EmployeeInput.PayBasis), refused.Errors.Keys);
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(PayBasis.Hourly, check.Profiles.Single(p => p.Id == id).PayBasis);
    }

    private async Task<EmployeeSaveResult> CreateAsync(string number, string? payBasis)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var input = new EmployeeInput { EmployeeNumber = number, FirstName = "Ada", LastName = number, HireDate = new DateTime(2020, 1, 1) };
        if (payBasis is not null)
        {
            input.PayBasis = payBasis;
        }

        return await new EmployeeService(db).CreateAsync(input, CancellationToken.None);
    }
}
