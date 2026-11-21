using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>The exception groups list: who sees which groups, the filters, the paging contract (CLAUDE.md §2.9) and what each viewer may do.</summary>
public sealed class AttendanceExceptionGroupQueryTests : AttendanceEngineTestBase
{
    private readonly int _managerId;
    private readonly int _otherEmployeeId;
    private readonly int _strangerId;

    public AttendanceExceptionGroupQueryTests()
    {
        _managerId = AddProfile("M-1", "Grace", "Hopper", managerId: null);
        _otherEmployeeId = AddProfile("E-2", "Charles", "Babbage", managerId: _managerId);
        _strangerId = AddProfile("E-3", "Edsger", "Dijkstra", managerId: null);

        // Ada (the base employee) reports to nobody; Charles reports to Grace.
        AddGroup(EmployeeId, Monday, AttendanceExceptionStatus.Open, AttendanceExceptionType.LongBreak, AttendanceExceptionType.LateBreak);
        AddGroup(_otherEmployeeId, Monday, AttendanceExceptionStatus.PendingCorrection, AttendanceExceptionType.MissingPunch);
        AddGroup(_strangerId, Monday.AddDays(1), AttendanceExceptionStatus.Open, AttendanceExceptionType.NoShow);
        AddGroup(EmployeeId, Monday.AddDays(-3), AttendanceExceptionStatus.Closed, AttendanceExceptionType.LongBreak);
    }

    [Fact]
    public async Task AnEmployee_SeesOnlyTheirOwn_AManagerTheirReports_AndHrEveryone()
    {
        var own = await ListAsync(Viewer(EmployeeId, employee: true), ExceptionStatusFilter.All);
        var reports = await ListAsync(Viewer(_managerId, manager: true), ExceptionStatusFilter.All);
        var hr = await ListAsync(Viewer(_strangerId, hr: true), ExceptionStatusFilter.All);
        var none = await ListAsync(Viewer(_strangerId), ExceptionStatusFilter.All);

        Assert.Equal([EmployeeId, EmployeeId], own.Items.Select(g => g.EmployeeId));
        Assert.Equal([_otherEmployeeId], reports.Items.Select(g => g.EmployeeId));
        Assert.Equal(4, hr.Total);
        Assert.Empty(none.Items);
        Assert.Equal(0, none.Total);
    }

    [Fact]
    public async Task TheStatusDateAndEmployeeFilters_Narrow_AndTheListIsNewestFirst()
    {
        var hr = Viewer(_strangerId, hr: true);

        var all = await ListAsync(hr, ExceptionStatusFilter.All);
        Assert.Equal(all.Items.OrderByDescending(g => g.AttendanceDate).Select(g => g.AttendanceDate), all.Items.Select(g => g.AttendanceDate));
        Assert.Equal(3, (await ListAsync(hr, ExceptionStatusFilter.Active)).Total);
        Assert.Equal(2, (await ListAsync(hr, ExceptionStatusFilter.Open)).Total);
        Assert.Equal(_otherEmployeeId, Assert.Single((await ListAsync(hr, ExceptionStatusFilter.PendingCorrection)).Items).EmployeeId);
        Assert.Equal("Closed", Assert.Single((await ListAsync(hr, ExceptionStatusFilter.Closed)).Items).Status);
        Assert.Equal(2, (await ListAsync(hr, ExceptionStatusFilter.All, from: Monday, to: Monday)).Total);
        Assert.Equal(2, (await ListAsync(hr, ExceptionStatusFilter.All, employeeId: EmployeeId)).Total);
    }

    [Fact]
    public async Task EachGroup_CarriesItsExceptions()
    {
        var hr = Viewer(_strangerId, hr: true);

        var groups = (await ListAsync(hr, ExceptionStatusFilter.All)).Items;

        var ada = groups.Single(g => g.EmployeeId == EmployeeId && g.AttendanceDate == Monday);
        Assert.Equal(["LongBreak", "LateBreak"], ada.Exceptions.Select(e => e.ExceptionType));
        Assert.All(ada.Exceptions, e => Assert.Equal(ada.Id, e.ExceptionGroupId));
        Assert.Equal(2, ada.ActiveCount);
        Assert.Equal("Ada Lovelace", ada.EmployeeName);
    }

    [Fact]
    public async Task AGroupCanBeAcceptedByTheManagerOrHr_OnlyWhenNoProblemNeedsAPunch_NeverByTheEmployee()
    {
        AddGroup(_otherEmployeeId, Monday.AddDays(5), AttendanceExceptionStatus.Open, AttendanceExceptionType.LongBreak, AttendanceExceptionType.MissingPunch);
        AddGroup(_otherEmployeeId, Monday.AddDays(6), AttendanceExceptionStatus.Open, AttendanceExceptionType.LongBreak, AttendanceExceptionType.ShortHours);

        var asManager = (await ListAsync(Viewer(_managerId, manager: true), ExceptionStatusFilter.Active)).Items;
        var asHr = (await ListAsync(Viewer(900, hr: true), ExceptionStatusFilter.Active)).Items;
        var asEmployee = (await ListAsync(Viewer(_otherEmployeeId, employee: true), ExceptionStatusFilter.Active)).Items;
        var asOtherManager = (await ListAsync(Viewer(901, manager: true), ExceptionStatusFilter.Active)).Items;

        // Monday: waiting for a correction; +5: a missing punch is in it; +6: both can be accepted.
        Assert.Equal([false, false, true], asManager.OrderBy(g => g.AttendanceDate).Select(g => g.CanAccept));
        Assert.Equal([false, true, false, true], asHr.OrderBy(g => g.AttendanceDate).Where(g => g.EmployeeId == _otherEmployeeId || g.EmployeeId == _strangerId).Select(g => g.CanAccept));
        Assert.All(asEmployee, g => Assert.False(g.CanAccept));
        Assert.Empty(asOtherManager);
    }

    [Fact]
    public async Task AnEmployeeCanCorrectTheirOwnOpenGroup_NotAWaitingOne_AndNotADayWhereAnotherShiftWaits()
    {
        // Ada's other shift on the same day is waiting for a correction, so the open one cannot be corrected until it is decided.
        AddGroup(_otherEmployeeId, Monday, AttendanceExceptionStatus.Open, AttendanceExceptionType.LongBreak);
        var own = (await ListAsync(Viewer(EmployeeId, employee: true), ExceptionStatusFilter.Active)).Items.Single();
        var theirs = (await ListAsync(Viewer(_otherEmployeeId, employee: true), ExceptionStatusFilter.Active)).Items.OrderBy(g => g.Id).ToList();

        Assert.True(own.CanCorrect);
        Assert.False(own.IsWaiting);
        Assert.All(theirs, g => Assert.False(g.CanCorrect));
        Assert.All(theirs, g => Assert.True(g.IsWaiting));
        Assert.Equal(2, theirs.Count);
    }

    [Fact]
    public async Task ThePagesTogether_AreTheWholeListOnce_InOrder_AndTheTotalFollowsTheFilters()
    {
        for (var day = 1; day <= 21; day++)
        {
            AddGroup(_otherEmployeeId, Monday.AddDays(10 + day), AttendanceExceptionStatus.Open, AttendanceExceptionType.LongBreak);
        }

        var hr = Viewer(_strangerId, hr: true);
        var ids = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await ListAsync(hr, ExceptionStatusFilter.All, page: page, pageSize: 10);
            Assert.Equal((25, page, 10), (result.Total, result.Page, result.PageSize));
            ids.AddRange(result.Items.Select(g => g.Id));
        }

        Assert.Equal(25, ids.Count);
        Assert.Equal(25, ids.Distinct().Count());
        var whole = await ListAsync(hr, ExceptionStatusFilter.All, pageSize: 200);
        Assert.Equal(whole.Items.Select(g => g.Id), ids);

        var narrowed = await ListAsync(hr, ExceptionStatusFilter.All, employeeId: _otherEmployeeId, pageSize: 10);
        Assert.Equal((22, 10), (narrowed.Total, narrowed.Items.Count));
    }

    [Fact]
    public async Task APagePastTheEnd_AnswersTheLastPage_AndTheSizeIsClamped()
    {
        var hr = Viewer(_strangerId, hr: true);

        var past = await ListAsync(hr, ExceptionStatusFilter.All, page: 9, pageSize: 3);
        var defaulted = await ListAsync(hr, ExceptionStatusFilter.All, pageSize: 0);
        var capped = await ListAsync(hr, ExceptionStatusFilter.All, pageSize: 5000);
        var floor = await ListAsync(hr, ExceptionStatusFilter.All, page: -4, pageSize: -1);

        Assert.Equal((2, 1), (past.Page, past.Items.Count));
        Assert.Equal(20, defaulted.PageSize);
        Assert.Equal(200, capped.PageSize);
        Assert.Equal((1, 20), (floor.Page, floor.PageSize));
    }

    private static ExceptionViewer Viewer(int id, bool employee = false, bool manager = false, bool hr = false) => new(id, employee, manager, hr);

    private async Task<PagedResult<ExceptionGroupListItem>> ListAsync(
        ExceptionViewer viewer,
        ExceptionStatusFilter status,
        DateTime? from = null,
        DateTime? to = null,
        int? employeeId = null,
        int page = 1,
        int pageSize = 100)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceExceptionGroupQuery(db).ListAsync(new ExceptionGroupFilter(status, from, to, employeeId, page, pageSize), viewer, CancellationToken.None);
    }

    private int AddProfile(string number, string firstName, string lastName, int? managerId)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var profile = new Profile { EmployeeNumber = number, FirstName = firstName, LastName = lastName, Title = $"{firstName} {lastName}", ManagerId = managerId };
        db.Add(profile);
        db.SaveChanges();
        return profile.Id;
    }

    /// <summary>A group with one exception of each type given, each in the status of the group.</summary>
    private void AddGroup(int employeeId, DateTime date, AttendanceExceptionStatus status, params AttendanceExceptionType[] types)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var group = new AttendanceExceptionGroup { EmployeeId = employeeId, AttendanceDate = date, Status = status };
        db.AttendanceExceptionGroups.Add(group);
        foreach (var type in types)
        {
            db.AttendanceExceptions.Add(new AttendanceException
            {
                EmployeeId = employeeId,
                AttendanceDate = date,
                ExceptionGroup = group,
                ExceptionType = type,
                MissingEventType = type == AttendanceExceptionType.MissingPunch ? NormalizedEventType.CheckOut : null,
                Status = status,
                Description = $"{type}.",
            });
        }

        db.SaveChanges();
    }
}
