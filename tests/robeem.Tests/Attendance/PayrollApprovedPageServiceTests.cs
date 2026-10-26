using System.Globalization;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

namespace robeem.Tests.Attendance;

/// <summary>The paged view of the payroll gate: the days are the extract's, the page only orders, totals and cuts them.</summary>
public sealed class PayrollApprovedPageServiceTests
{
    [Fact]
    public void TheDays_AreOrderedByEmployeeNumberThenDate_AndCutIntoPages()
    {
        var days = new[] { Day(2, "E-2", 1), Day(1, "E-1", 3), Day(1, "E-1", 1), Day(1, "E-1", 2), Day(2, "E-2", 2) };

        var one = PayrollApprovedPageService.Build(days, 1, 2);
        var three = PayrollApprovedPageService.Build(days, 3, 2);

        Assert.Equal((5, 1, 2), (one.Days.Total, one.Days.Page, one.Days.PageSize));
        Assert.Equal([("E-1", 1), ("E-1", 2)], one.Days.Items.Select(d => (d.EmployeeNumber, d.AttendanceDate.Day)));
        Assert.Equal([("E-2", 2)], three.Days.Items.Select(d => (d.EmployeeNumber, d.AttendanceDate.Day)));
    }

    [Fact]
    public void TheTotals_CoverEveryDay_WhicheverPageIsAsked()
    {
        var days = new[] { Day(1, "E-1", 1, working: 8, overtime: 1), Day(1, "E-1", 2, working: 7.5), Day(2, "E-2", 1, working: 8) };

        var page = PayrollApprovedPageService.Build(days, 1, 1);

        Assert.Equal(["E-1", "E-2"], page.Employees.Select(e => e.EmployeeNumber));
        Assert.Equal((2, TimeSpan.FromHours(15.5), TimeSpan.FromHours(1)), (page.Employees[0].Days, page.Employees[0].WorkingHours, page.Employees[0].OvertimeHours));
        Assert.Single(page.Days.Items);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-5, 20)]
    [InlineData(1, 1)]
    [InlineData(5000, 200)]
    public void ThePageSize_DefaultsToTwenty_AndIsClampedToTwoHundred(int asked, int expected)
    {
        Assert.Equal(expected, PayrollApprovedPageService.Build([], 1, asked).Days.PageSize);
    }

    [Fact]
    public void APagePastTheEnd_AnswersTheLastPage_AndNothingIsAnEmptyFirstPage()
    {
        var days = Enumerable.Range(1, 5).Select(d => Day(1, "E-1", d)).ToList();

        var past = PayrollApprovedPageService.Build(days, 99, 2);
        var none = PayrollApprovedPageService.Build([], 4, 20);

        Assert.Equal((3, 1), (past.Days.Page, past.Days.Items.Count));
        Assert.Equal((1, 0, 0), (none.Days.Page, none.Days.Total, none.Employees.Count));
    }

    [Fact]
    public async Task TheService_PagesWhatTheExtractAllows_AndAsksItForTheWholeRange()
    {
        var extract = new FakeExtract([Day(1, "E-1", 1), Day(1, "E-1", 2), Day(1, "E-1", 3)]);

        var page = await new PayrollApprovedPageService(extract).GetPageAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), null, 2, 2, CancellationToken.None);

        Assert.Equal((new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)), extract.Asked);
        Assert.Equal((3, 2, 1), (page.Days.Total, page.Days.Page, page.Days.Items.Count));
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData("", 3)]
    [InlineData("   ", 3)]
    [InlineData("e-2", 2)]
    [InlineData("  E-2  ", 2)]
    [InlineData("lovelace", 1)]
    [InlineData("EMPLOYEE", 2)]
    [InlineData("nobody", 0)]
    public void TheEmployeeFilter_MatchesANumberOrANameAnywhereInIt_IgnoringCase(string? employee, int expectedDays)
    {
        var days = new[]
        {
            Day(1, "E-1", 1, name: "Ada Lovelace"),
            Day(2, "E-2", 1, name: "Employee Two"),
            Day(2, "E-2", 2, name: "Employee Two"),
        };

        Assert.Equal(expectedDays, PayrollApprovedPageService.Filter(days, employee).Count);
    }

    [Theory]
    [InlineData("2026-10-01", "2026-10-31", null, null)]
    [InlineData("2026-10-01", "2026-10-31", "E-1", null)]
    [InlineData(null, null, "E-1", null)]
    [InlineData("2026-10-31", "2026-10-31", null, null)]
    [InlineData(null, null, null, "Give an employee, or a pay period or dates.")]
    [InlineData(null, null, "   ", "Give an employee, or a pay period or dates.")]
    [InlineData("2026-10-01", null, "E-1", "Give both dates, or leave both empty to look at every date of one employee.")]
    [InlineData(null, "2026-10-31", "E-1", "Give both dates, or leave both empty to look at every date of one employee.")]
    [InlineData("2026-10-31", "2026-10-01", "E-1", "The period ends before it starts.")]
    public void AQuery_NeedsAnEmployeeOrBothDates_AndNeverJustOneDate(string? from, string? to, string? employee, string? refusal)
    {
        Assert.Equal(refusal, PayrollApprovedPageService.Validate(Date(from), Date(to), employee));
    }

    [Fact]
    public async Task AnEmployeeAlone_ReadsEveryDate_WhileAQueryWithNeitherIsRefusedBeforeTheExtractIsAsked()
    {
        var extract = new FakeExtract([Day(1, "E-1", 1), Day(2, "E-2", 1)]);
        var service = new PayrollApprovedPageService(extract);

        var page = await service.GetPageAsync(null, null, "E-1", 1, 20, CancellationToken.None);
        var csv = await service.GetDaysAsync(null, null, "E-2", CancellationToken.None);

        Assert.Equal((PayrollApprovedPageService.AllDatesStart, PayrollApprovedPageService.AllDatesEnd), extract.Asked);
        Assert.Equal(("E-2", 1), (extract.AskedEmployee, csv.Count));
        Assert.Equal(["E-1"], page.Employees.Select(e => e.EmployeeNumber));

        var fresh = new FakeExtract([]);
        await Assert.ThrowsAsync<ArgumentException>(() => new PayrollApprovedPageService(fresh).GetPageAsync(null, null, null, 1, 20, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => new PayrollApprovedPageService(fresh).GetDaysAsync(new DateTime(2026, 10, 1), null, "E-1", CancellationToken.None));
        Assert.Null(fresh.Asked);
    }

    [Fact]
    public async Task TheTotalsAndThePages_FollowTheEmployeeFilter()
    {
        var extract = new FakeExtract([Day(1, "E-1", 1), Day(2, "E-2", 1), Day(2, "E-2", 2), Day(2, "E-2", 3)]);

        var page = await new PayrollApprovedPageService(extract).GetPageAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), "E-2", 2, 2, CancellationToken.None);

        Assert.Equal((3, 2, 1), (page.Days.Total, page.Days.Page, page.Days.Items.Count));
        var only = Assert.Single(page.Employees);
        Assert.Equal(("E-2", 3), (only.EmployeeNumber, only.Days));
    }

    private static DateTime? Date(string? value) => value is null ? null : DateTime.Parse(value, CultureInfo.InvariantCulture);

    private static PayrollAttendanceDay Day(int employeeId, string number, int dayOfOctober, double working = 8, double overtime = 0, string? name = null) =>
        new(employeeId, number, name ?? $"Employee {number}", new DateTime(2026, 10, dayOfOctober), TimeSpan.FromHours(working), TimeSpan.FromHours(overtime), TimeSpan.Zero, []);

    private sealed class FakeExtract : IPayrollAttendanceExtract
    {
        private readonly IReadOnlyList<PayrollAttendanceDay> _days;

        public FakeExtract(IReadOnlyList<PayrollAttendanceDay> days)
        {
            _days = days;
        }

        public (DateTime From, DateTime To)? Asked { get; private set; }

        public string? AskedEmployee { get; private set; }

        public Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedRecordsAsync(DateTime payPeriodStart, DateTime payPeriodEnd, CancellationToken cancellationToken) =>
            GetApprovedRecordsAsync(payPeriodStart, payPeriodEnd, null, cancellationToken);

        public Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedRecordsAsync(DateTime payPeriodStart, DateTime payPeriodEnd, string? employee, CancellationToken cancellationToken)
        {
            Asked = (payPeriodStart, payPeriodEnd);
            AskedEmployee = employee;
            return Task.FromResult(PayrollApprovedPageService.Filter(_days, employee));
        }
    }
}
