using System.Net;
using Microsoft.AspNetCore.Mvc;
using robeem.Application.HR.Employees;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Employees;
using robeem.DataModel;
using robeem.Tests.Web;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;

namespace robeem.Tests.HR;

/// <summary>The employees list sorted by a column: by employee number to begin with, and by any heading the user presses.</summary>
public sealed class EmployeeSortTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task TheListStartsByEmployeeNumber_InNaturalOrder_AndCanBeTurnedRound()
    {
        Add("10");
        Add("E-10");
        Add("9");
        Add("100");
        Add("E-2");

        var ascending = await ListAsync(EmployeeSortField.Number, false);
        var descending = await ListAsync(EmployeeSortField.Number, true);
        var starting = await ListAsync(null, null);

        // Shorter numbers first, so 9 comes before 10, and E-2 before E-10.
        Assert.Equal(["9", "10", "100", "E-2", "E-10"], ascending);
        Assert.Equal(ascending.AsEnumerable().Reverse(), descending);
        Assert.Equal(ascending, starting);
    }

    [Fact]
    public async Task EachColumnSortsAscendingAndDescending_AndWhatIsEmptyComesLastEitherWay()
    {
        Add("1", "Ada", "Lovelace", department: "Engineering", jobTitle: "Analyst", hired: new DateTime(2020, 1, 1));
        Add("2", "Grace", "Hopper", department: null, jobTitle: "Admiral", hired: new DateTime(2018, 6, 1), active: false);
        Add("3", "Alan", "Turing", department: "Operations", jobTitle: null, hired: new DateTime(2022, 3, 1));

        // By last name, then first name: Hopper, Lovelace, Turing.
        Assert.Equal(["2", "1", "3"], await ListAsync(EmployeeSortField.Name, false));
        Assert.Equal(["3", "1", "2"], await ListAsync(EmployeeSortField.Name, true));
        Assert.Equal(["1", "3", "2"], await ListAsync(EmployeeSortField.Department, false));
        Assert.Equal(["3", "1", "2"], await ListAsync(EmployeeSortField.Department, true));
        Assert.Equal(["2", "1", "3"], await ListAsync(EmployeeSortField.JobTitle, false));
        Assert.Equal(["1", "2", "3"], await ListAsync(EmployeeSortField.JobTitle, true));
        Assert.Equal(["2", "1", "3"], await ListAsync(EmployeeSortField.HireDate, false));
        Assert.Equal(["3", "1", "2"], await ListAsync(EmployeeSortField.HireDate, true));

        // Active before inactive, ascending; the number settles the tie.
        Assert.Equal(["1", "3", "2"], await ListAsync(EmployeeSortField.Status, false));
        Assert.Equal(["2", "1", "3"], await ListAsync(EmployeeSortField.Status, true));
    }

    [Fact]
    public async Task TheTimeScheduleAndManagerColumns_SortBySchedulesTitleAndManagersName_NoneLast()
    {
        var office = AddSchedule("Office");
        var night = AddSchedule("Night");
        var boss = Add("1", "Zed", "Boss");
        var other = Add("2", "Amy", "Chief");
        Add("3", scheduleId: office, managerId: other);
        Add("4", scheduleId: night, managerId: boss);
        Add("5");

        Assert.Equal(["4", "3", "1", "2", "5"], await ListAsync(EmployeeSortField.TimeSchedule, false));
        Assert.Equal(["3", "4", "1", "2", "5"], await ListAsync(EmployeeSortField.TimeSchedule, true));
        Assert.Equal(["3", "4", "1", "2", "5"], await ListAsync(EmployeeSortField.Manager, false));
        Assert.Equal(["4", "3", "1", "2", "5"], await ListAsync(EmployeeSortField.Manager, true));
    }

    [Theory]
    [InlineData(EmployeeSortField.Number)]
    [InlineData(EmployeeSortField.Name)]
    [InlineData(EmployeeSortField.Department)]
    [InlineData(EmployeeSortField.JobTitle)]
    [InlineData(EmployeeSortField.TimeSchedule)]
    [InlineData(EmployeeSortField.Manager)]
    [InlineData(EmployeeSortField.HireDate)]
    [InlineData(EmployeeSortField.Status)]
    public async Task ThePagesTogether_AreTheWholeListOnce_InTheSortedOrder_ForEveryColumnAndDirection(EmployeeSortField field)
    {
        var schedule = AddSchedule("Office");
        for (var i = 1; i <= 7; i++)
        {
            // Many ties on purpose: the same department, title and hire date, so only the number can settle them.
            Add($"E-{i}", "Same", $"Name{i % 2}", department: i % 3 == 0 ? null : "Ops", jobTitle: "Clerk", hired: new DateTime(2024, 1, 1), active: i % 4 != 0, scheduleId: i % 2 == 0 ? schedule : null);
        }

        foreach (var descending in new[] { false, true })
        {
            var whole = await ListAsync(field, descending);
            var pages = new List<string>();
            for (var page = 1; page <= 4; page++)
            {
                pages.AddRange(await ListAsync(field, descending, page, 2));
            }

            Assert.Equal(7, whole.Count);
            Assert.Equal(whole, pages);
        }
    }

    [Theory]
    [InlineData(null, null, EmployeeSortField.Number, false)]
    [InlineData("name", "desc", EmployeeSortField.Name, true)]
    [InlineData("HIREDATE", "asc", EmployeeSortField.HireDate, false)]
    [InlineData("nonsense", "desc", EmployeeSortField.Number, true)]
    [InlineData("99", "sideways", EmployeeSortField.Number, false)]
    public async Task TheScreen_SortsByNumberByDefault_AndAnUnknownColumnFallsBackToIt(string? sort, string? dir, EmployeeSortField expected, bool descending)
    {
        var employees = new FakeEmployees();

        var model = (EmployeeIndexViewModel)((ViewResult)await new EmployeesController(employees, null!).Index("ada", true, 1, 20, sort, dir, CancellationToken.None)).Model!;

        Assert.Equal((expected, descending), (employees.SortAsked, employees.DescendingAsked));
        Assert.Equal((expected, descending), (model.Sort, model.Descending));
    }

    [Fact]
    public void AColumnHeading_SortsAscendingFirst_ThenTurnsRound_AndKeepsTheSearch_AndStartsAtTheFirstPage()
    {
        var byNumber = new EmployeeIndexViewModel { Search = "ada", IncludeInactive = true, PageSize = 50, Page = 3 };
        var byName = new EmployeeIndexViewModel { Search = "ada", IncludeInactive = true, PageSize = 50, Page = 3, Sort = EmployeeSortField.Name };
        var byNameDescending = new EmployeeIndexViewModel { Sort = EmployeeSortField.Name, Descending = true };

        // The column the list is sorted by, ascending, turns round; any other starts ascending; neither carries a page.
        Assert.Equal("desc", byNumber.SortLink(EmployeeSortField.Number)["dir"]);
        Assert.Equal("asc", byNumber.SortLink(EmployeeSortField.Name)["dir"]);
        Assert.Equal("desc", byName.SortLink(EmployeeSortField.Name)["dir"]);
        Assert.Equal("asc", byNameDescending.SortLink(EmployeeSortField.Name)["dir"]);
        Assert.Equal(
            new Dictionary<string, string?> { ["pageSize"] = "50", ["search"] = "ada", ["includeInactive"] = "true", ["sort"] = "name", ["dir"] = "asc" },
            byNumber.SortLink(EmployeeSortField.Name));
        Assert.DoesNotContain("page", byNumber.SortLink(EmployeeSortField.Name).Keys);

        // Every page link keeps the sort, unless it is the starting one.
        Assert.False(byNumber.Pager.RouteValues.ContainsKey("sort"));
        Assert.Equal(("name", "asc"), (byName.Pager.RouteValues["sort"], byName.Pager.RouteValues["dir"]));
        Assert.Equal("desc", byNameDescending.Pager.RouteValues["dir"]);

        Assert.Equal(("ascending", "none"), (byName.AriaSort(EmployeeSortField.Name), byName.AriaSort(EmployeeSortField.Number)));
        Assert.Equal("descending", byNameDescending.AriaSort(EmployeeSortField.Name));
    }

    [Fact]
    public async Task TheApiClient_AsksForASort_OnlyWhenItIsNotTheStartingOne()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """{"items":[],"total":0,"page":1,"pageSize":20}""");
        var client = new EmployeesApiClient(handler.CreateClient());

        await client.ListAsync(null, false, 1, 20, EmployeeSortField.Number, false, CancellationToken.None);
        await client.ListAsync("ada", false, 2, 20, EmployeeSortField.HireDate, true, CancellationToken.None);

        Assert.Equal(
            ["/api/employees?includeInactive=false&page=1&pageSize=20", "/api/employees?includeInactive=false&page=2&pageSize=20&search=ada&sort=HireDate&descending=true"],
            handler.Requests.Select(r => r.RequestUri!.PathAndQuery));
    }

    private async Task<List<string>> ListAsync(EmployeeSortField? sort, bool? descending, int page = 1, int pageSize = 100)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = new EmployeeService(db);
        var result = sort is { } field
            ? await service.ListAsync(null, true, page, pageSize, field, descending == true, CancellationToken.None)
            : await service.ListAsync(null, true, page, pageSize, CancellationToken.None);
        return result.Items.Select(e => e.EmployeeNumber).ToList();
    }

    private int Add(string number, string first = "Ada", string last = "Lovelace", string? department = null, string? jobTitle = null, DateTime? hired = null, bool active = true, int? scheduleId = null, int? managerId = null)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var profile = new Profile
        {
            EmployeeNumber = number,
            FirstName = first,
            LastName = last,
            Title = $"{first} {last}",
            Department = department,
            JobTitle = jobTitle,
            HireDate = hired ?? new DateTime(2020, 1, 1),
            IsActive = active,
            ManagerId = managerId,
        };
        db.Profiles.Add(profile);
        db.SaveChanges();
        if (scheduleId is { } id)
        {
            var team = db.TimeScheduleTeams.First(t => t.TimeScheduleId == id);
            db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { EmployeeId = profile.Id, TimeScheduleId = id, TeamId = team.Id });
            db.SaveChanges();
        }

        return profile.Id;
    }

    private int AddSchedule(string title)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var schedule = new TimeSchedule { Title = title };
        db.TimeScheduleTeams.Add(new TimeScheduleTeam { Title = "All", TimeSchedule = schedule, AllInclusive = true });
        db.SaveChanges();
        return schedule.Id;
    }

    private sealed class FakeEmployees : IEmployeesApiClient
    {
        public EmployeeSortField SortAsked { get; private set; }

        public bool DescendingAsked { get; private set; }

        public Task<PagedResult<EmployeeListItem>> ListAsync(string? search, bool includeInactive, int page, int pageSize, EmployeeSortField sort, bool descending, CancellationToken cancellationToken)
        {
            (SortAsked, DescendingAsked) = (sort, descending);
            return Task.FromResult(new PagedResult<EmployeeListItem> { Items = [], Total = 0, Page = 1, PageSize = pageSize });
        }

        public Task<EmployeeInput?> GetAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ScheduleOption>> ListSchedulesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ManagerOption>> ListManagerOptionsAsync(int? excludeEmployeeId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<EmployeeLookupItem>> LookupAsync(string? search, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<EmployeeSaveResult> CreateAsync(EmployeeInput input, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<EmployeeSaveResult> UpdateAsync(int id, EmployeeInput input, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
