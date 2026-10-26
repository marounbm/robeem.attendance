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

        var page = await new PayrollApprovedPageService(extract).GetPageAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), 2, 2, CancellationToken.None);

        Assert.Equal((new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)), extract.Asked);
        Assert.Equal((3, 2, 1), (page.Days.Total, page.Days.Page, page.Days.Items.Count));
    }

    private static PayrollAttendanceDay Day(int employeeId, string number, int dayOfOctober, double working = 8, double overtime = 0) =>
        new(employeeId, number, $"Employee {number}", new DateTime(2026, 10, dayOfOctober), TimeSpan.FromHours(working), TimeSpan.FromHours(overtime), TimeSpan.Zero, []);

    private sealed class FakeExtract : IPayrollAttendanceExtract
    {
        private readonly IReadOnlyList<PayrollAttendanceDay> _days;

        public FakeExtract(IReadOnlyList<PayrollAttendanceDay> days)
        {
            _days = days;
        }

        public (DateTime From, DateTime To)? Asked { get; private set; }

        public Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedRecordsAsync(DateTime payPeriodStart, DateTime payPeriodEnd, CancellationToken cancellationToken)
        {
            Asked = (payPeriodStart, payPeriodEnd);
            return Task.FromResult(_days);
        }
    }
}
