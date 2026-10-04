using Microsoft.EntityFrameworkCore;
using robeem.Application.HR.Employees;
using robeem.Contracts.HR.Employees;
using robeem.DataModel;

namespace robeem.Tests.HR;

public sealed class EmployeeServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Create_AssignsTheScheduleAllInclusiveTeam()
    {
        var (scheduleId, defaultTeamId) = await AddScheduleAsync("Week", withDefaultTeam: true);

        var result = await CreateAsync(Input("E-1", scheduleId));

        Assert.True(result.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var assignment = await db.TimeScheduleEmployees.SingleAsync();
        Assert.Equal(result.EmployeeId, assignment.EmployeeId);
        Assert.Equal(scheduleId, assignment.TimeScheduleId);
        Assert.Equal(defaultTeamId, assignment.TeamId);
        Assert.Equal("Ada Lovelace", (await db.Profiles.SingleAsync()).Title);
    }

    [Fact]
    public async Task Create_DuplicateEmployeeNumber_IsRejected()
    {
        Assert.True((await CreateAsync(Input("E-1", null))).Succeeded);

        var duplicate = await CreateAsync(Input(" E-1 ", null));

        Assert.False(duplicate.Succeeded);
        Assert.Contains(nameof(EmployeeInput.EmployeeNumber), duplicate.Errors.Keys);
    }

    [Fact]
    public async Task Create_TerminationBeforeHire_IsRejected()
    {
        var input = Input("E-1", null);
        input.TerminationDate = input.HireDate.AddDays(-1);

        var result = await CreateAsync(input);

        Assert.Contains(nameof(EmployeeInput.TerminationDate), result.Errors.Keys);
    }

    [Fact]
    public async Task Create_ScheduleWithoutAllInclusiveTeam_IsRejectedAndNothingIsSaved()
    {
        var (scheduleId, _) = await AddScheduleAsync("No default team", withDefaultTeam: false);

        var result = await CreateAsync(Input("E-1", scheduleId));

        Assert.Contains(nameof(EmployeeInput.TimeScheduleId), result.Errors.Keys);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.Profiles);
    }

    [Fact]
    public async Task Update_MovingToAnotherSchedule_UsesItsDefaultTeam_AndClearingRemovesAssignment()
    {
        var (firstId, _) = await AddScheduleAsync("First", withDefaultTeam: true);
        var (secondId, secondTeamId) = await AddScheduleAsync("Second", withDefaultTeam: true);
        var created = await CreateAsync(Input("E-1", firstId));

        var moved = await UpdateAsync(created.EmployeeId!.Value, Input("E-1", secondId));

        Assert.True(moved.Succeeded);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var assignment = await db.TimeScheduleEmployees.SingleAsync();
            Assert.Equal((secondId, secondTeamId), (assignment.TimeScheduleId, assignment.TeamId));
        }

        Assert.True((await UpdateAsync(created.EmployeeId.Value, Input("E-1", null))).Succeeded);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.TimeScheduleEmployees);
    }

    [Fact]
    public async Task Update_UnknownEmployee_ReportsNotFound()
    {
        Assert.True((await UpdateAsync(999, Input("E-1", null))).NotFound);
    }

    [Fact]
    public async Task List_SearchesAndHidesInactive_AndIsTenantScoped()
    {
        await CreateAsync(Input("E-1", null));
        var inactive = Input("E-2", null);
        inactive.FirstName = "Grace";
        inactive.IsActive = false;
        await CreateAsync(inactive);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = new EmployeeService(db);
        Assert.Single(await service.ListAsync(null, includeInactive: false, CancellationToken.None));
        Assert.Equal(2, (await service.ListAsync(null, includeInactive: true, CancellationToken.None)).Count);
        Assert.Equal("E-2", Assert.Single(await service.ListAsync("Grace", includeInactive: true, CancellationToken.None)).EmployeeNumber);
        Assert.Equal("E-2", Assert.Single(await service.ListAsync("gRACE", includeInactive: true, CancellationToken.None)).EmployeeNumber);

        using var otherTenant = _database.CreateContext(TestDatabase.TenantB);
        Assert.Empty(await new EmployeeService(otherTenant).ListAsync(null, includeInactive: true, CancellationToken.None));
    }

    private static EmployeeInput Input(string number, int? scheduleId) => new()
    {
        EmployeeNumber = number,
        FirstName = "Ada",
        LastName = "Lovelace",
        HireDate = new DateTime(2026, 1, 5),
        TimeScheduleId = scheduleId,
    };

    private async Task<EmployeeSaveResult> CreateAsync(EmployeeInput input)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await new EmployeeService(db).CreateAsync(input, CancellationToken.None);
    }

    private async Task<EmployeeSaveResult> UpdateAsync(int id, EmployeeInput input)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await new EmployeeService(db).UpdateAsync(id, input, CancellationToken.None);
    }

    private async Task<(int ScheduleId, int TeamId)> AddScheduleAsync(string title, bool withDefaultTeam)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var schedule = new TimeSchedule { Title = title };
        var team = new TimeScheduleTeam { Title = "Team", TimeSchedule = schedule, AllInclusive = withDefaultTeam };
        db.AddRange(schedule, team);
        await db.SaveChangesAsync();
        return (schedule.Id, team.Id);
    }
}
