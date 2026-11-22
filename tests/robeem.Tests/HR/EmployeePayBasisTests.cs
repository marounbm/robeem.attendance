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

    [Fact]
    public async Task AnOnDemandSchedule_TakesOnlyEmployeesPaidByTheHour()
    {
        var scheduleId = AddSchedule(onDemand: true);
        var regularId = AddSchedule(onDemand: false);

        var hourly = await CreateAsync("E-1", "Hourly", scheduleId);
        var scheduled = await CreateAsync("E-2", "Scheduled", scheduleId);
        var onRegular = await CreateAsync("E-3", "Scheduled", regularId);

        Assert.True(hourly.Succeeded);
        Assert.Contains("on demand", scheduled.Errors[nameof(EmployeeInput.TimeScheduleId)]);
        Assert.True(onRegular.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(["E-1", "E-3"], db.TimeScheduleEmployees.Select(a => a.Employee!.EmployeeNumber).OrderBy(n => n));
        var options = await new EmployeeService(db).ListSchedulesAsync(CancellationToken.None);
        Assert.Equal([(true, scheduleId), (false, regularId)], options.OrderByDescending(o => o.IsOnDemand).Select(o => (o.IsOnDemand, o.Id)));
    }

    [Fact]
    public async Task AnEmployeeOnAnOnDemandSchedule_CannotBeChangedToBePaidByTheSchedule()
    {
        var scheduleId = AddSchedule(onDemand: true);
        var id = (await CreateAsync("E-1", "Hourly", scheduleId)).EmployeeId!.Value;

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var input = (await new EmployeeService(db).GetAsync(id, CancellationToken.None))!;
            input.PayBasis = "Scheduled";
            var refused = await new EmployeeService(db).UpdateAsync(id, input, CancellationToken.None);
            Assert.Contains(nameof(EmployeeInput.TimeScheduleId), refused.Errors.Keys);

            input.TimeScheduleId = null;
            Assert.True((await new EmployeeService(db).UpdateAsync(id, input, CancellationToken.None)).Succeeded);
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal((PayBasis.Scheduled, 0), (check.Profiles.Single().PayBasis, check.TimeScheduleEmployees.Count()));
    }

    private int AddSchedule(bool onDemand)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var schedule = new TimeSchedule { Title = onDemand ? "Hourly staff" : "Office" };
        db.TimeScheduleTeams.Add(new TimeScheduleTeam { Title = "All", TimeSchedule = schedule, AllInclusive = true });
        if (onDemand)
        {
            db.OnDemandTimeSchedules.Add(new OnDemandTimeSchedule { TimeSchedule = schedule });
        }

        db.SaveChanges();
        return schedule.Id;
    }

    private async Task<EmployeeSaveResult> CreateAsync(string number, string? payBasis, int? scheduleId = null)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var input = new EmployeeInput { EmployeeNumber = number, FirstName = "Ada", LastName = number, HireDate = new DateTime(2020, 1, 1), TimeScheduleId = scheduleId };
        if (payBasis is not null)
        {
            input.PayBasis = payBasis;
        }

        return await new EmployeeService(db).CreateAsync(input, CancellationToken.None);
    }
}
