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
        Assert.Single((await service.ListAsync(null, includeInactive: false, 1, 100, CancellationToken.None)).Items);
        Assert.Equal(2, (await service.ListAsync(null, includeInactive: true, 1, 100, CancellationToken.None)).Items.Count);
        Assert.Equal("E-2", Assert.Single((await service.ListAsync("Grace", includeInactive: true, 1, 100, CancellationToken.None)).Items).EmployeeNumber);
        Assert.Equal("E-2", Assert.Single((await service.ListAsync("gRACE", includeInactive: true, 1, 100, CancellationToken.None)).Items).EmployeeNumber);

        using var otherTenant = _database.CreateContext(TestDatabase.TenantB);
        var elsewhere = await new EmployeeService(otherTenant).ListAsync(null, includeInactive: true, 1, 100, CancellationToken.None);
        Assert.Equal((0, 0), (elsewhere.Items.Count, elsewhere.Total));
    }

    [Fact]
    public async Task List_IsPaged_InTheOrderOfTheNames_WithTheTotalOfTheSearch()
    {
        foreach (var (number, first, last) in new[] { ("E-1", "Ada", "Lovelace"), ("E-2", "Grace", "Hopper"), ("E-3", "Alan", "Turing"), ("E-4", "Edsger", "Dijkstra"), ("E-5", "Barbara", "Liskov") })
        {
            await CreateAsync(Input(number, null, first, last));
        }

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = new EmployeeService(db);
        var pageOne = await service.ListAsync(null, includeInactive: true, 1, 2, CancellationToken.None);
        var pageTwo = await service.ListAsync(null, includeInactive: true, 2, 2, CancellationToken.None);
        var pageThree = await service.ListAsync(null, includeInactive: true, 3, 2, CancellationToken.None);
        var beyond = await service.ListAsync(null, includeInactive: true, 99, 2, CancellationToken.None);
        var searched = await service.ListAsync("a", includeInactive: true, 1, 1, CancellationToken.None);
        var nobody = await service.ListAsync("zzz", includeInactive: true, 4, 10, CancellationToken.None);

        // Together the pages are everyone once, by last name; every page knows the whole list.
        Assert.All(new[] { pageOne, pageTwo, pageThree }, p => Assert.Equal((5, 2, 3), (p.Total, p.PageSize, p.PageCount)));
        Assert.Equal(["Edsger Dijkstra", "Grace Hopper", "Barbara Liskov", "Ada Lovelace", "Alan Turing"], pageOne.Items.Concat(pageTwo.Items).Concat(pageThree.Items).Select(e => e.FullName));
        Assert.Equal((3, 1), (beyond.Page, beyond.Items.Count));
        Assert.Equal(5, searched.Total);
        Assert.Equal((0, 1, 0), (nobody.Total, nobody.Page, nobody.Items.Count));
        Assert.Equal(20, (await service.ListAsync(null, includeInactive: true, 1, 0, CancellationToken.None)).PageSize);
        Assert.Equal(200, (await service.ListAsync(null, includeInactive: true, 1, 100000, CancellationToken.None)).PageSize);
    }

    [Fact]
    public async Task Create_WithAManager_StoresIt_AndTheListAndEditShowIt()
    {
        var boss = (await CreateAsync(Input("B-1", null, "Grace", "Hopper"))).EmployeeId!.Value;

        var report = await CreateAsync(Input("E-1", null, managerId: boss));

        Assert.True(report.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = new EmployeeService(db);
        Assert.Equal(boss, (await service.GetAsync(report.EmployeeId!.Value, CancellationToken.None))!.ManagerId);
        var listed = (await service.ListAsync(null, includeInactive: true, 1, 100, CancellationToken.None)).Items.Single(e => e.EmployeeNumber == "E-1");
        Assert.Equal("Grace Hopper", listed.ManagerName);
    }

    [Fact]
    public async Task AManager_CanBeChanged_AndRemoved()
    {
        var boss = (await CreateAsync(Input("B-1", null, "Grace", "Hopper"))).EmployeeId!.Value;
        var other = (await CreateAsync(Input("B-2", null, "Alan", "Turing"))).EmployeeId!.Value;
        var id = (await CreateAsync(Input("E-1", null, managerId: boss))).EmployeeId!.Value;

        Assert.True((await UpdateAsync(id, Input("E-1", null, managerId: other))).Succeeded);
        Assert.True((await UpdateAsync(id, Input("E-1", null, managerId: null))).Succeeded);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Null((await new EmployeeService(db).GetAsync(id, CancellationToken.None))!.ManagerId);
    }

    [Fact]
    public async Task ANobodyManager_ASelfManager_AnUnknownOne_AndAnInactiveOne_AreRefused()
    {
        var id = (await CreateAsync(Input("E-1", null))).EmployeeId!.Value;
        var inactive = Input("B-9", null, "Old", "Boss");
        inactive.IsActive = false;
        var inactiveId = (await CreateAsync(inactive)).EmployeeId!.Value;

        var self = await UpdateAsync(id, Input("E-1", null, managerId: id));
        var unknown = await UpdateAsync(id, Input("E-1", null, managerId: 999));
        var notActive = await UpdateAsync(id, Input("E-1", null, managerId: inactiveId));

        Assert.Contains("own manager", self.Errors[nameof(EmployeeInput.ManagerId)]);
        Assert.Contains("does not exist", unknown.Errors[nameof(EmployeeInput.ManagerId)]);
        Assert.Contains("not active", notActive.Errors[nameof(EmployeeInput.ManagerId)]);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Null((await new EmployeeService(db).GetAsync(id, CancellationToken.None))!.ManagerId);
    }

    [Fact]
    public async Task AReportingLoop_IsRefused_AtAnyDepth()
    {
        var a = (await CreateAsync(Input("A", null))).EmployeeId!.Value;
        var b = (await CreateAsync(Input("B", null, managerId: a))).EmployeeId!.Value;
        var c = (await CreateAsync(Input("C", null, managerId: b))).EmployeeId!.Value;

        // A -> C would close the loop A -> C -> B -> A.
        var loop = await UpdateAsync(a, Input("A", null, managerId: c));

        Assert.Contains("loop", loop.Errors[nameof(EmployeeInput.ManagerId)]);
    }

    [Fact]
    public async Task ManagerOptions_AreTheActiveEmployees_WithoutTheOneBeingEdited()
    {
        var a = (await CreateAsync(Input("A", null, "Ada", "Aa"))).EmployeeId!.Value;
        await CreateAsync(Input("B", null, "Ben", "Bb"));
        var gone = Input("C", null, "Cy", "Cc");
        gone.IsActive = false;
        await CreateAsync(gone);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = new EmployeeService(db);

        Assert.Equal(["Ada Aa (A)", "Ben Bb (B)"], (await service.ListManagerOptionsAsync(null, CancellationToken.None)).Select(o => o.Name));
        Assert.Equal(["Ben Bb (B)"], (await service.ListManagerOptionsAsync(a, CancellationToken.None)).Select(o => o.Name));
    }

    [Fact]
    public async Task AManagerFromAnotherTenant_DoesNotExist()
    {
        using (var other = _database.CreateContext(TestDatabase.TenantB))
        {
            var created = await new EmployeeService(other).CreateAsync(Input("B-1", null, "Grace", "Hopper"), CancellationToken.None);
            Assert.True(created.Succeeded);
        }

        int foreignId;
        using (var db = _database.CreateContext(TestDatabase.TenantB))
        {
            foreignId = db.Profiles.Single().Id;
        }

        var result = await CreateAsync(Input("E-1", null, managerId: foreignId));

        Assert.Contains("does not exist", result.Errors[nameof(EmployeeInput.ManagerId)]);
    }

    private static EmployeeInput Input(string number, int? scheduleId, string firstName = "Ada", string lastName = "Lovelace", int? managerId = null) => new()
    {
        ManagerId = managerId,
        FirstName = firstName,
        LastName = lastName,
        EmployeeNumber = number,
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
