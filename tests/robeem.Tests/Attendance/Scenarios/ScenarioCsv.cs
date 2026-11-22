using System.Globalization;
using System.Text;
using robeem.DataModel;

namespace robeem.Tests.Attendance.Scenarios;

/// <summary>The Generic CSV file of one employee: one row per punch, as a clock exports them.</summary>
internal static class ScenarioCsv
{
    public const string DeviceId = "SCN-1";

    private const string Header = "transaction_id,employee_id,timestamp,type,device_id";

    public static string Build(string employeeNumber, IEnumerable<DayPlan> plans)
    {
        var text = new StringBuilder(Header).Append("\r\n");
        var rows = plans
            .SelectMany(p => p.Punches)
            .Select((punch, index) => (punch, index))
            .OrderBy(r => r.punch.At)
            .ThenBy(r => r.index);

        var number = 0;
        foreach (var (punch, _) in rows)
        {
            number++;
            text.Append(CultureInfo.InvariantCulture, $"{employeeNumber}-{number:0000},{employeeNumber},{punch.At:yyyy-MM-dd HH:mm},{Label(punch.Type)},{DeviceId}\r\n");
        }

        return text.ToString();
    }

    private static string Label(NormalizedEventType type) => type switch
    {
        NormalizedEventType.CheckIn => "IN",
        NormalizedEventType.CheckOut => "OUT",
        NormalizedEventType.BreakOut => "BREAK OUT",
        NormalizedEventType.BreakIn => "BREAK IN",
        _ => string.Empty,
    };
}
