using System.Globalization;
using System.Text;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>The compensation overview as a CSV file: one line per employee for their pay period, hours as decimals with two places.</summary>
public static class CompensationCsv
{
    public const string Header = "EmployeeNumber,Employee,Schedule,PeriodStart,PeriodEnd,Closed,PaidDays,WorkedHours,RegularHours,OvertimeHours,UndertimeHours,"
        + "OvertimeCountedHours,UndertimeRemainingHours,BalanceHours,OvertimeLineHours,DeficitHours,UnpaidLeaveHours,PenaltyMinutes,UndecidedLines,NotReadyDays";

    public static string Build(IEnumerable<CompensationOverviewItem> items)
    {
        var text = new StringBuilder(Header).Append("\r\n");
        foreach (var item in items.OrderBy(i => i.EmployeeNumber.Length).ThenBy(i => i.EmployeeNumber, StringComparer.Ordinal))
        {
            var s = item.Summary;
            text.Append(PayrollCsv.Field(item.EmployeeNumber)).Append(',')
                .Append(PayrollCsv.Field(item.EmployeeName)).Append(',')
                .Append(PayrollCsv.Field(item.ScheduleName)).Append(',')
                .Append(item.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(item.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(item.IsClosed ? "yes" : "no").Append(',')
                .Append(Number(s.PaidDays)).Append(',')
                .Append(Hours(s.WorkedMinutes)).Append(',')
                .Append(Hours(s.RegularMinutes)).Append(',')
                .Append(Hours(s.OvertimeMinutes)).Append(',')
                .Append(Hours(s.UndertimeMinutes)).Append(',')
                .Append(Hours(s.OvertimeCountedMinutes)).Append(',')
                .Append(Hours(s.UndertimeRemainingMinutes)).Append(',')
                .Append(Hours(s.BalanceMinutes)).Append(',')
                .Append(Hours(s.OvertimeLineMinutes)).Append(',')
                .Append(Hours(s.DeficitMinutes)).Append(',')
                .Append(Hours(s.UndertimeUnpaidLeaveMinutes)).Append(',')
                .Append(Number(s.PenaltyMinutes)).Append(',')
                .Append(Number(s.UndecidedLines)).Append(',')
                .Append(Number(item.NotReadyDays))
                .Append("\r\n");
        }

        return text.ToString();
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Hours(int minutes) => Math.Round(minutes / 60.0, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
}
