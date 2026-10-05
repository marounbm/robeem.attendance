using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>The exceptions list (screens phase 1): who sees which exceptions, and the filters.</summary>
public sealed class AttendanceExceptionQueryTests : AttendanceEngineTestBase
{
    private readonly int _managerId;
    private readonly int _otherEmployeeId;
    private readonly int _strangerId;

    public AttendanceExceptionQueryTests()
    {
        _managerId = AddProfile("M-1", "Grace", "Hopper", managerId: null);
        _otherEmployeeId = AddProfile("E-2", "Charles", "Babbage", managerId: _managerId);
        _strangerId = AddProfile("E-3", "Edsger", "Dijkstra", managerId: null);

        // Ada (the base employee) reports to nobody; Charles reports to Grace.
        AddException(EmployeeId, Monday, AttendanceExceptionStatus.Open);
        AddException(_otherEmployeeId, Monday, AttendanceExceptionStatus.PendingCorrection);
        AddException(_strangerId, Monday.AddDays(1), AttendanceExceptionStatus.Open);
        AddException(EmployeeId, Monday.AddDays(-3), AttendanceExceptionStatus.Closed);
    }

    [Fact]
    public async Task AnEmployee_SeesOnlyTheirOwn()
    {
        var own = await ListAsync(new ExceptionViewer(EmployeeId, IsEmployee: true, IsManager: false, IsHr: false), ExceptionStatusFilter.All);

        Assert.All(own, e => Assert.Equal(EmployeeId, e.EmployeeId));
        Assert.Equal(2, own.Count);
    }

    [Fact]
    public async Task AManager_SeesTheirDirectReports_NotTheirOwn_UnlessTheyAreAlsoAnEmployee()
    {
        Assert.Equal([_otherEmployeeId], (await ListAsync(new ExceptionViewer(_managerId, IsEmployee: false, IsManager: true, IsHr: false), ExceptionStatusFilter.All)).Select(e => e.EmployeeId));
    }

    [Fact]
    public async Task AUserInTwoRoles_SeesTheUnion()
    {
        AddException(_managerId, Monday, AttendanceExceptionStatus.Open);

        var both = await ListAsync(new ExceptionViewer(_managerId, IsEmployee: true, IsManager: true, IsHr: false), ExceptionStatusFilter.All);

        Assert.Equal([_managerId, _otherEmployeeId], both.Select(e => e.EmployeeId).Order());
    }

    [Fact]
    public async Task Hr_SeesEveryone()
    {
        var all = await ListAsync(new ExceptionViewer(_strangerId, IsEmployee: false, IsManager: false, IsHr: true), ExceptionStatusFilter.All);

        Assert.Equal(4, all.Count);
    }

    [Fact]
    public async Task ARoleWithNoScope_SeesNothing()
    {
        Assert.Empty(await ListAsync(new ExceptionViewer(_strangerId, IsEmployee: false, IsManager: false, IsHr: false), ExceptionStatusFilter.All));
    }

    [Fact]
    public async Task TheStatusFilter_DefaultsToTheActiveOnes()
    {
        var hr = new ExceptionViewer(_strangerId, false, false, true);

        Assert.Equal(3, (await ListAsync(hr, ExceptionStatusFilter.Active)).Count);
        Assert.Equal(2, (await ListAsync(hr, ExceptionStatusFilter.Open)).Count);
        Assert.Equal(_otherEmployeeId, Assert.Single(await ListAsync(hr, ExceptionStatusFilter.PendingCorrection)).EmployeeId);
        var closed = Assert.Single(await ListAsync(hr, ExceptionStatusFilter.Closed));
        Assert.Equal((EmployeeId, "Closed"), (closed.EmployeeId, closed.Status));
    }

    [Fact]
    public async Task TheDateEmployeeAndTakeFilters_Narrow_AndTheListIsNewestFirst()
    {
        var hr = new ExceptionViewer(_strangerId, false, false, true);

        var all = await ListAsync(hr, ExceptionStatusFilter.All);
        Assert.Equal(all.OrderByDescending(e => e.AttendanceDate).Select(e => e.AttendanceDate), all.Select(e => e.AttendanceDate));

        Assert.Equal(2, (await ListAsync(hr, ExceptionStatusFilter.All, from: Monday, to: Monday)).Count);
        Assert.Single(await ListAsync(hr, ExceptionStatusFilter.All, from: Monday.AddDays(1)));
        Assert.Equal(2, (await ListAsync(hr, ExceptionStatusFilter.All, employeeId: EmployeeId)).Count);
        Assert.Single(await ListAsync(hr, ExceptionStatusFilter.All, take: 1));
        Assert.Single(await ListAsync(hr, ExceptionStatusFilter.All, take: -5));
    }

    [Fact]
    public async Task OnlyTheEmployeesOwnOpenMissingPunch_CanBeProposed()
    {
        var items = await ListAsync(new ExceptionViewer(EmployeeId, IsEmployee: true, IsManager: true, IsHr: true), ExceptionStatusFilter.All);

        Assert.Equal([true], items.Where(e => e.EmployeeId == EmployeeId && e.Status == "Open").Select(e => e.CanPropose));
        Assert.All(items.Where(e => e.EmployeeId != EmployeeId || e.Status != "Open"), e => Assert.False(e.CanPropose));
    }

    [Fact]
    public async Task AnInvalidSequence_IsNeverProposable_AndTheShiftIsNamed()
    {
        AssignSchedule(StandardDay);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        AddPunch(NormalizedEventType.BreakOut, Monday.At(12, 0));
        AddPunch(NormalizedEventType.BreakIn, Monday.At(13, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 30));
        Clock.Local = Monday.At(23, 0);
        await ProcessAsync(Monday);

        var items = await ListAsync(new ExceptionViewer(EmployeeId, true, false, false), ExceptionStatusFilter.Active);

        var invalid = items.Single(e => e.ExceptionType == "InvalidSequence");
        Assert.Equal(("08:00-17:00", false), (invalid.ShiftTitle, invalid.CanPropose));
        Assert.Null(invalid.MissingEventType);
    }

    private async Task<IReadOnlyList<robeem.Contracts.Attendance.ExceptionListItem>> ListAsync(
        ExceptionViewer viewer,
        ExceptionStatusFilter status,
        DateTime? from = null,
        DateTime? to = null,
        int? employeeId = null,
        int take = 100)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceExceptionQuery(db).ListAsync(new ExceptionFilter(status, from, to, employeeId, take), viewer, CancellationToken.None);
    }

    private int AddProfile(string number, string firstName, string lastName, int? managerId)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var profile = new Profile { EmployeeNumber = number, FirstName = firstName, LastName = lastName, Title = $"{firstName} {lastName}", ManagerId = managerId };
        db.Add(profile);
        db.SaveChanges();
        return profile.Id;
    }

    private void AddException(int employeeId, DateTime date, AttendanceExceptionStatus status)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.AttendanceExceptions.Add(new AttendanceException
        {
            EmployeeId = employeeId,
            AttendanceDate = date,
            ExceptionType = AttendanceExceptionType.MissingPunch,
            MissingEventType = NormalizedEventType.CheckOut,
            Status = status,
            Description = "A check-out is missing.",
        });
        db.SaveChanges();
    }
}
