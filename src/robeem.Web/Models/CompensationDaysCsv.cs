using System.Globalization;
using System.Text;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>
/// The days of the compensation report as a CSV file: one line per employee and day payroll may pay for, with the hours, the overtime or under time
/// the day gave, the penalty minutes and what was decided. Hours are decimals with two places. It replaces the old list of approved days: the same days,
/// read for each employee's own period, with the decisions, and still there once the period is closed.
/// </summary>
public static class CompensationDaysCsv
{
    public const string Header = "EmployeeNumber,Employee,PeriodStart,PeriodEnd,PeriodClosed,Date,Scheduled,Worked,WorkedHours,BreakHours,OvertimeKind,OvertimeHours,"
        + "UndertimeHours,CoveredByLeaveHours,LateMinutes,EarlyMinutes,PenaltyMinutes,Decisions,InferredPunches";

    public static string Build(IEnumerable<CompensationDayRow> rows)
    {
        var text = new StringBuilder(Header).Append("\r\n");
        foreach (var row in rows.OrderBy(r => r.EmployeeNumber.Length).ThenBy(r => r.EmployeeNumber, StringComparer.Ordinal).ThenBy(r => r.Day.Date))
        {
            var d = row.Day;
            text.Append(PayrollCsv.Field(row.EmployeeNumber)).Append(',')
                .Append(PayrollCsv.Field(row.EmployeeName)).Append(',')
                .Append(Date(row.PeriodStart)).Append(',')
                .Append(Date(row.PeriodEnd)).Append(',')
                .Append(row.PeriodIsClosed ? "yes" : "no").Append(',')
                .Append(Date(d.Date)).Append(',')
                .Append(PayrollCsv.Field(d.Scheduled)).Append(',')
                .Append(PayrollCsv.Field(d.Worked)).Append(',')
                .Append(Hours(d.WorkedMinutes)).Append(',')
                .Append(Hours(d.BreakMinutes)).Append(',')
                .Append(d.OvertimeKind).Append(',')
                .Append(Hours(d.OvertimeMinutes)).Append(',')
                .Append(Hours(d.UndertimeMinutes)).Append(',')
                .Append(Hours(d.CoveredByLeaveMinutes)).Append(',')
                .Append(Number(d.LateMinutes)).Append(',')
                .Append(Number(d.EarlyMinutes)).Append(',')
                .Append(Number(d.PenaltyMinutes)).Append(',')
                .Append(PayrollCsv.Field(d.Decisions)).Append(',')
                .Append(d.HasInferredPunches ? "yes" : "no")
                .Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>The days of one employee's report, with their number, name and period on every line.</summary>
    public static string Build(CompensationReport report) =>
        Build(report.Days.Select(day => new CompensationDayRow
        {
            EmployeeId = report.EmployeeId,
            EmployeeNumber = report.EmployeeNumber,
            EmployeeName = report.EmployeeName,
            ScheduleName = report.ScheduleName,
            PeriodStart = report.PeriodStart,
            PeriodEnd = report.PeriodEnd,
            PeriodIsClosed = report.IsClosed,
            Day = day,
        }));

    private static string Date(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Hours(int minutes) => Math.Round(minutes / 60.0, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
}
