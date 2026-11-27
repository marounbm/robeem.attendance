using robeem.Application.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// An employee's own pay period: made when payroll closes it, only that employee's days leave the payroll gate, and the company-wide periods keep
/// working for everyone.
/// </summary>
public sealed class EmployeePayPeriodTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 1);
    private static readonly DateTime End = new(2026, 10, 31);

    private readonly TestDatabase _database = new();
    private readonly int _ada;
    private readonly int _grace;

    public EmployeePayPeriodTests()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var ada = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        var grace = new Profile { EmployeeNumber = "E-2", FirstName = "Grace", LastName = "Hopper", Title = "Grace Hopper" };
        db.AddRange(ada, grace);
        db.SaveChanges();
        (_ada, _grace) = (ada.Id, grace.Id);
        AddApprovedDay(db, ada.Id, new DateTime(2026, 10, 5));
        AddApprovedDay(db, grace.Id, new DateTime(2026, 10, 5));
        db.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ClosingOneEmployeesPeriod_TakesOnlyTheirDaysOutOfThePayrollGate()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            Assert.True((await Periods(db).CloseForEmployeeAsync(_ada, Start, End, CancellationToken.None)).Succeeded);
        }

        var days = await ExtractAsync(Start, End);

        Assert.Equal([_grace], days.Select(d => d.EmployeeId));
        using var check = _database.CreateContext(TestDatabase.TenantA);
        var period = Assert.Single(check.PayPeriods);
        Assert.Equal((_ada, true, Start, End), (period.EmployeeId, period.IsClosed, period.StartDate, period.EndDate));
        Assert.Contains("for employee", Assert.Single(check.AttendanceAuditLogs).Details);
    }

    [Fact]
    public async Task ACompanyWidePeriodThatIsClosed_StillTakesEveryonesDaysOut()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.PayPeriods.Add(new PayPeriod { StartDate = Start, EndDate = End, IsClosed = true });
            db.SaveChanges();
        }

        Assert.Empty(await ExtractAsync(Start, End));
    }

    [Fact]
    public async Task TheReportOfAClosedPeriod_CanStillReadItsDays_ButPayrollCannot()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            await Periods(db).CloseForEmployeeAsync(_ada, Start, End, CancellationToken.None);
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var extract = new PayrollAttendanceExtract(check, new AttendancePolicyResolver(check));

        Assert.Equal(2, (await extract.GetApprovedRecordsAsync(Start, End, [_ada, _grace], true, CancellationToken.None)).Count);
        Assert.Equal([_grace], (await extract.GetApprovedRecordsAsync(Start, End, [_ada, _grace], false, CancellationToken.None)).Select(d => d.EmployeeId));
        Assert.Empty(await extract.GetApprovedRecordsAsync(Start, End, [], false, CancellationToken.None));
    }

    [Fact]
    public async Task ClosingTwiceIsRefused_ADifferentOverlappingPeriodIsRefused_AndAnUnknownEmployeeIsMissing()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        await Periods(db).CloseForEmployeeAsync(_ada, Start, End, CancellationToken.None);

        var again = await Periods(db).CloseForEmployeeAsync(_ada, Start, End, CancellationToken.None);
        var overlapping = await Periods(db).CloseForEmployeeAsync(_ada, new DateTime(2026, 10, 15), new DateTime(2026, 11, 14), CancellationToken.None);
        var backwards = await Periods(db).CloseForEmployeeAsync(_grace, End, Start, CancellationToken.None);

        Assert.Contains("already closed", again.Errors["id"]);
        Assert.Contains("overlaps another of this employee's", overlapping.Errors.Values.Single());
        Assert.Contains("ends before it starts", backwards.Errors.Values.Single());
        Assert.True((await Periods(db).CloseForEmployeeAsync(9999, Start, End, CancellationToken.None)).NotFound);
        Assert.Single(db.PayPeriods);
    }

    [Fact]
    public async Task TwoEmployeesCanCloseTheSameDates_AndACompanyWidePeriodIsStillListedAlone()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        await Periods(db).CloseForEmployeeAsync(_ada, Start, End, CancellationToken.None);
        await Periods(db).CloseForEmployeeAsync(_grace, Start, End, CancellationToken.None);
        await Periods(db).CreateAsync(new robeem.Contracts.Attendance.PayPeriodInput { StartDate = Start, EndDate = End }, CancellationToken.None);

        var listed = await Periods(db).ListAsync(CancellationToken.None);

        Assert.Equal(3, db.PayPeriods.Count());
        Assert.Single(listed);
    }

    [Fact]
    public async Task AClosedEmployeePeriod_CanBeReopened_AndThenItsDaysAreReadAgain()
    {
        int id;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            id = (await Periods(db).CloseForEmployeeAsync(_ada, Start, End, CancellationToken.None)).Id!.Value;
            Assert.True((await Periods(db).ReopenAsync(id, "A day was missing.", CancellationToken.None)).Succeeded);
        }

        Assert.Equal(2, (await ExtractAsync(Start, End)).Count);

        // Closing it again finds the same row.
        using var again = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(id, (await Periods(again).CloseForEmployeeAsync(_ada, Start, End, CancellationToken.None)).Id);
        Assert.Single(again.PayPeriods);
    }

    private static void AddApprovedDay(RobeemDbContext db, int employeeId, DateTime date)
    {
        db.AttendanceDaySummaries.Add(new AttendanceDaySummary { EmployeeId = employeeId, AttendanceDate = date, Status = AttendanceRecordStatus.Approved, TotalWorkingHours = TimeSpan.FromHours(8) });
        db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = employeeId, AttendanceDate = date, Status = AttendanceRecordStatus.Approved, WorkingHours = TimeSpan.FromHours(8) });
    }

    private static PayPeriodService Periods(RobeemDbContext db) =>
        new(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System));

    private async Task<IReadOnlyList<robeem.Contracts.Attendance.PayrollAttendanceDay>> ExtractAsync(DateTime from, DateTime to)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await new PayrollAttendanceExtract(db, new AttendancePolicyResolver(db)).GetApprovedRecordsAsync(from, to, (string?)null, CancellationToken.None);
    }
}
