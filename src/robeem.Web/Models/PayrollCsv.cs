using System.Globalization;
using System.Text;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>Writes the approved days of a payroll query as CSV, one line per employee day.</summary>
public static class PayrollCsv
{
    public const string Header = "EmployeeNumber,Date,WorkingHours,OvertimeHours,BreakHours,Shifts";

    /// <summary>Builds the file: a header, then the days by employee number and date. Hours are decimal, two places.</summary>
    public static string Build(IEnumerable<PayrollAttendanceDay> days)
    {
        var text = new StringBuilder(Header).Append("\r\n");
        foreach (var day in days.OrderBy(d => d.EmployeeNumber, StringComparer.Ordinal).ThenBy(d => d.AttendanceDate))
        {
            text.Append(Field(day.EmployeeNumber)).Append(',')
                .Append(day.AttendanceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(Decimal(day.TotalWorkingHours)).Append(',')
                .Append(Decimal(day.TotalOvertimeHours)).Append(',')
                .Append(Decimal(day.TotalBreakHours)).Append(',')
                .Append(day.Shifts.Count.ToString(CultureInfo.InvariantCulture))
                .Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// Escapes one text field: quoted when it holds a comma, quote or line break, and prefixed with an
    /// apostrophe when a spreadsheet would read it as a formula (a leading <c>=</c>, <c>+</c>, <c>-</c>
    /// or <c>@</c>, tab or carriage return).
    /// </summary>
    public static string Field(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }

    private static string Decimal(TimeSpan duration) => Math.Round(duration.TotalHours, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
}
