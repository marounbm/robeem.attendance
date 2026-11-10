using robeem.Application.Common;
using robeem.Application.HR.Leaves;
using robeem.Contracts.HR.Leaves;
using robeem.DataModel;

namespace robeem.Tests.HR;

public sealed class LeaveServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly int _ada;
    private readonly int _ben;

    public LeaveServiceTests()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var ada = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        var ben = new Profile { EmployeeNumber = "E-2", FirstName = "Ben", LastName = "Franklin", Title = "Ben Franklin" };
        db.AddRange(ada, ben);
        db.SaveChanges();
        (_ada, _ben) = (ada.Id, ben.Id);
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AWholeDayLeave_IsSaved_AndShownWithTheEmployee()
    {
        var result = await CreateAsync(_ada, new DateTime(2026, 8, 3), new DateTime(2026, 8, 7), notes: " Summer ");

        Assert.True(result.Succeeded);
        var leave = (await Service().GetAsync(result.Id!.Value, CancellationToken.None))!;
        Assert.Equal(("Ada Lovelace", "E-1", "Annual", false, "Summer"), (leave.EmployeeName, leave.EmployeeNumber, leave.LeaveType, leave.IsPartDay, leave.Notes));
        Assert.Equal(string.Empty, leave.StartTime);
        Assert.Equal(string.Empty, leave.EndTime);
        Assert.Equal((new DateTime(2026, 8, 3), new DateTime(2026, 8, 7)), (leave.StartDate, leave.EndDate));
    }

    [Fact]
    public async Task APartDayLeave_KeepsItsTimes_OnOneDayOnly()
    {
        var saved = await CreateAsync(_ada, new DateTime(2026, 8, 3), new DateTime(2026, 8, 3), part: ("9:30", "12:00"));
        Assert.True(saved.Succeeded);
        var leave = (await Service().GetAsync(saved.Id!.Value, CancellationToken.None))!;
        Assert.Equal((true, "09:30", "12:00"), (leave.IsPartDay, leave.StartTime, leave.EndTime));

        var range = await CreateAsync(_ada, new DateTime(2026, 9, 1), new DateTime(2026, 9, 2), part: ("09:00", "10:00"));
        Assert.Contains(nameof(LeaveInput.EndDate), range.Errors.Keys);
    }

    [Fact]
    public async Task BadInput_IsRefusedWithTheFieldNamed()
    {
        Assert.Contains(nameof(LeaveInput.EmployeeId), (await CreateAsync(9999, new DateTime(2026, 8, 3), new DateTime(2026, 8, 3))).Errors.Keys);
        Assert.Contains(nameof(LeaveInput.EndDate), (await CreateAsync(_ada, new DateTime(2026, 8, 5), new DateTime(2026, 8, 4))).Errors.Keys);
        Assert.Contains(nameof(LeaveInput.EndDate), (await CreateAsync(_ada, new DateTime(2026, 1, 1), new DateTime(2027, 6, 1))).Errors.Keys);
        Assert.Contains(nameof(LeaveInput.LeaveType), (await CreateAsync(_ada, new DateTime(2026, 8, 3), new DateTime(2026, 8, 3), type: "Holiday")).Errors.Keys);
        Assert.Contains(nameof(LeaveInput.StartTime), (await CreateAsync(_ada, new DateTime(2026, 8, 3), new DateTime(2026, 8, 3), part: ("25:00", "12:00"))).Errors.Keys);
        Assert.Contains(nameof(LeaveInput.EndTime), (await CreateAsync(_ada, new DateTime(2026, 8, 3), new DateTime(2026, 8, 3), part: ("09:00", "09:00"))).Errors.Keys);
        Assert.Contains(nameof(LeaveInput.EndTime), (await CreateAsync(_ada, new DateTime(2026, 8, 3), new DateTime(2026, 8, 3), part: ("09:00", null))).Errors.Keys);
        Assert.Contains(nameof(LeaveInput.Notes), (await CreateAsync(_ada, new DateTime(2026, 8, 3), new DateTime(2026, 8, 3), notes: new string('x', 501))).Errors.Keys);
        Assert.Empty((await Service().ListAsync(Filter(), CancellationToken.None)).Items);
    }

    [Fact]
    public async Task WholeDayTimesAreDropped_EvenIfPosted()
    {
        var saved = await Service().CreateAsync(
            new LeaveInput { EmployeeId = _ada, LeaveType = "Sick", StartDate = new DateTime(2026, 8, 3), EndDate = new DateTime(2026, 8, 3), IsPartDay = false, StartTime = "09:00", EndTime = "10:00" },
            CancellationToken.None);

        var leave = (await Service().GetAsync(saved.Id!.Value, CancellationToken.None))!;
        Assert.Equal((false, string.Empty, string.Empty), (leave.IsPartDay, leave.StartTime, leave.EndTime));
    }

    [Fact]
    public async Task LeavesOfOneEmployee_MayNotOverlap_ButOthersMay()
    {
        await CreateAsync(_ada, new DateTime(2026, 8, 3), new DateTime(2026, 8, 7));

        // Whole day against whole day: touching ranges overlap, the next day does not.
        Assert.Contains(nameof(LeaveInput.StartDate), (await CreateAsync(_ada, new DateTime(2026, 8, 7), new DateTime(2026, 8, 9))).Errors.Keys);
        Assert.True((await CreateAsync(_ada, new DateTime(2026, 8, 8), new DateTime(2026, 8, 9))).Succeeded);

        // Part of a day against a whole day, and against another part of the same day.
        Assert.Contains(nameof(LeaveInput.StartDate), (await CreateAsync(_ada, new DateTime(2026, 8, 4), new DateTime(2026, 8, 4), part: ("09:00", "10:00"))).Errors.Keys);
        Assert.True((await CreateAsync(_ada, new DateTime(2026, 8, 10), new DateTime(2026, 8, 10), part: ("09:00", "10:00"))).Succeeded);
        Assert.Contains(nameof(LeaveInput.StartDate), (await CreateAsync(_ada, new DateTime(2026, 8, 10), new DateTime(2026, 8, 10), part: ("09:30", "11:00"))).Errors.Keys);
        Assert.True((await CreateAsync(_ada, new DateTime(2026, 8, 10), new DateTime(2026, 8, 10), part: ("10:00", "11:00"))).Succeeded);

        // Another employee is free on the same days.
        Assert.True((await CreateAsync(_ben, new DateTime(2026, 8, 3), new DateTime(2026, 8, 7))).Succeeded);
    }

    [Fact]
    public async Task APartDayLeaveThatRunsPastMidnight_OverlapsTheNextDaysEarlyHours()
    {
        Assert.True((await CreateAsync(_ada, new DateTime(2026, 8, 3), new DateTime(2026, 8, 3), part: ("22:00", "02:00"))).Succeeded);

        Assert.Contains(nameof(LeaveInput.StartDate), (await CreateAsync(_ada, new DateTime(2026, 8, 4), new DateTime(2026, 8, 4), part: ("01:00", "03:00"))).Errors.Keys);
        Assert.True((await CreateAsync(_ada, new DateTime(2026, 8, 4), new DateTime(2026, 8, 4), part: ("02:00", "03:00"))).Succeeded);
    }

    [Fact]
    public async Task EditingALeave_IsNotRefusedForOverlappingItself_ButIsForOverlappingAnother()
    {
        var first = (await CreateAsync(_ada, new DateTime(2026, 8, 3), new DateTime(2026, 8, 5))).Id!.Value;
        await CreateAsync(_ada, new DateTime(2026, 8, 10), new DateTime(2026, 8, 12));

        Assert.True((await UpdateAsync(first, _ada, new DateTime(2026, 8, 4), new DateTime(2026, 8, 6), type: "Sick")).Succeeded);
        Assert.Equal("Sick", (await Service().GetAsync(first, CancellationToken.None))!.LeaveType);
        Assert.Contains(nameof(LeaveInput.StartDate), (await UpdateAsync(first, _ada, new DateTime(2026, 8, 4), new DateTime(2026, 8, 11))).Errors.Keys);
        Assert.True((await UpdateAsync(9999, _ada, new DateTime(2026, 8, 4), new DateTime(2026, 8, 4))).NotFound);
    }

    [Fact]
    public async Task ALeave_CanBeRemoved_AndOnlyOnce()
    {
        var id = (await CreateAsync(_ada, new DateTime(2026, 8, 3), new DateTime(2026, 8, 3))).Id!.Value;

        Assert.True((await Service().DeleteAsync(id, CancellationToken.None)).Succeeded);
        Assert.True((await Service().DeleteAsync(id, CancellationToken.None)).NotFound);
        Assert.Null(await Service().GetAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task ThePages_TogetherAreTheWholeListOnce_NewestFirst()
    {
        var ids = await AddManyAsync(45);

        var seen = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await Service().ListAsync(Filter(page: page, pageSize: 20), CancellationToken.None);
            Assert.Equal((45, page, 20), (result.Total, result.Page, result.PageSize));
            seen.AddRange(result.Items.Select(l => l.Id));
        }

        Assert.Equal(45, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(ids.OrderByDescending(i => i), seen.OrderByDescending(i => i));

        // Newest first: by first day, then id.
        var all = new List<LeaveListItem>();
        for (var page = 1; page <= 3; page++)
        {
            all.AddRange((await Service().ListAsync(Filter(page: page, pageSize: 20), CancellationToken.None)).Items);
        }

        Assert.Equal(all.OrderByDescending(l => l.StartDate).ThenByDescending(l => l.Id).Select(l => l.Id), all.Select(l => l.Id));
    }

    [Fact]
    public async Task TheTotal_FollowsTheFilters()
    {
        await AddManyAsync(30);
        await CreateAsync(_ada, new DateTime(2025, 1, 6), new DateTime(2025, 1, 8), type: "Sick");

        Assert.Equal(31, (await Service().ListAsync(Filter(), CancellationToken.None)).Total);
        Assert.Equal(1, (await Service().ListAsync(Filter(type: "sick"), CancellationToken.None)).Total);
        Assert.Equal(1, (await Service().ListAsync(Filter(from: new DateTime(2025, 1, 1), to: new DateTime(2025, 12, 31)), CancellationToken.None)).Total);

        // The search matches the employee's name or number, ignoring case: Ada has 15 of the 30 and the sick leave, Ben 15.
        Assert.Equal(16, (await Service().ListAsync(Filter(search: "LOVELACE"), CancellationToken.None)).Total);
        Assert.Equal(15, (await Service().ListAsync(Filter(search: "franklin"), CancellationToken.None)).Total);
        Assert.Equal(15, (await Service().ListAsync(Filter(search: "e-2"), CancellationToken.None)).Total);
        Assert.Equal(15, (await Service().ListAsync(Filter(employeeId: _ben), CancellationToken.None)).Total);

        // A leave overlapping the range counts, even if it starts before it.
        Assert.Equal(1, (await Service().ListAsync(Filter(from: new DateTime(2025, 1, 7), to: new DateTime(2025, 1, 7)), CancellationToken.None)).Total);
    }

    [Fact]
    public async Task APagePastTheEnd_AnswersTheLastPage_AndTheSizeIsClamped()
    {
        await AddManyAsync(45);

        var past = await Service().ListAsync(Filter(page: 99, pageSize: 20), CancellationToken.None);
        Assert.Equal((3, 5), (past.Page, past.Items.Count));

        Assert.Equal(20, (await Service().ListAsync(Filter(pageSize: 0), CancellationToken.None)).PageSize);
        Assert.Equal(200, (await Service().ListAsync(Filter(pageSize: 5000), CancellationToken.None)).PageSize);
        Assert.Equal(1, (await Service().ListAsync(Filter(page: -4, pageSize: 1), CancellationToken.None)).Page);
        Assert.Equal(1, (await Service().ListAsync(Filter(employeeId: 12345), CancellationToken.None)).Page);
    }

    [Fact]
    public async Task AnotherTenantsLeaves_AreNotSeen()
    {
        await CreateAsync(_ada, new DateTime(2026, 8, 3), new DateTime(2026, 8, 3));

        using var other = _database.CreateContext(TestDatabase.TenantB);
        Assert.Empty((await new LeaveService(other).ListAsync(Filter(), CancellationToken.None)).Items);
    }

    private static LeaveFilter Filter(int? employeeId = null, string? search = null, DateTime? from = null, DateTime? to = null, string? type = null, int page = 1, int pageSize = 20) =>
        new(employeeId, search, from, to, type, page, pageSize);

    private static LeaveInput Input(int employeeId, DateTime start, DateTime end, string type, (string? From, string? To)? part, string? notes) => new()
    {
        EmployeeId = employeeId,
        LeaveType = type,
        StartDate = start,
        EndDate = end,
        IsPartDay = part is not null,
        StartTime = part?.From,
        EndTime = part?.To,
        Notes = notes,
    };

    private LeaveService Service() => new(_database.CreateContext(TestDatabase.TenantA));

    private Task<SaveResult> CreateAsync(int employeeId, DateTime start, DateTime end, string type = "Annual", (string? From, string? To)? part = null, string? notes = null) =>
        Service().CreateAsync(Input(employeeId, start, end, type, part, notes), CancellationToken.None);

    private Task<SaveResult> UpdateAsync(int id, int employeeId, DateTime start, DateTime end, string type = "Annual", (string? From, string? To)? part = null) =>
        Service().UpdateAsync(id, Input(employeeId, start, end, type, part, null), CancellationToken.None);

    /// <summary>Adds one-day leaves for the two employees on different days, some on the same first day so the tie-break is used.</summary>
    private async Task<List<int>> AddManyAsync(int count)
    {
        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var day = new DateTime(2026, 1, 1).AddDays(i / 2);
            var saved = await CreateAsync(i % 2 == 0 ? _ada : _ben, day, day);
            Assert.True(saved.Succeeded, string.Join("; ", saved.Errors.Values));
            ids.Add(saved.Id!.Value);
        }

        return ids;
    }
}
