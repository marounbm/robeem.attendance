using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using robeem.Application.Attendance;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// Reads punches from an uploaded CSV file. **Manual upload only**: there is no connection to
/// validate or authenticate, no device or employee list, and no incremental sync
/// (<see cref="GetTransactionChangesAsync"/> throws).
/// </summary>
/// <remarks>
/// <para>The file is RFC 4180 CSV with a header row. Each row becomes one transaction whose raw
/// payload is the row as a JSON object of header to text. The known columns are also copied into
/// the transaction's fields. Header matching ignores case, spaces and punctuation:</para>
/// <list type="bullet">
/// <item>employee: <c>employee_id</c>, <c>emp_id</c>, <c>badge_id</c>, <c>user_id</c>;</item>
/// <item>timestamp: <c>timestamp</c>, <c>datetime</c>, <c>punch_time</c>;</item>
/// <item>type: <c>type</c>, <c>event_type</c>, <c>punch_type</c>;</item>
/// <item>direction: <c>direction</c>, <c>in_out</c>;</item>
/// <item>device: <c>device_id</c>, <c>terminal_id</c>;</item>
/// <item>transaction id: <c>transaction_id</c>, <c>record_id</c>.</item>
/// </list>
/// <para>Timestamp and event-type formats are described on <see cref="GenericCsvInterpreter"/>.
/// Without an offset, a timestamp is a wall-clock time at the device.</para>
/// </remarks>
public sealed class GenericCsvProvider : IAttendanceProvider
{
    public const string ProviderKey = "generic-csv";

    private static readonly GenericCsvInterpreter Interpreter = new();

    private static readonly JsonSerializerOptions PayloadOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly string[] EmployeeColumns = ["employeeid", "empid", "badgeid", "userid"];
    private static readonly string[] TimestampColumns = ["timestamp", "datetime", "punchtime"];
    private static readonly string[] TypeColumns = ["type", "eventtype", "punchtype"];
    private static readonly string[] DirectionColumns = ["direction", "inout"];
    private static readonly string[] DeviceColumns = ["deviceid", "terminalid"];
    private static readonly string[] TransactionIdColumns = ["transactionid", "recordid"];

    public Task<ConnectionValidationResult> ValidateConnectionAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult(new ConnectionValidationResult(true, "Manual upload provider: there is no connection to validate."));

    public Task<AuthResult> AuthenticateAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult(new AuthResult(true));

    public Task<IReadOnlyList<ExternalDeviceDto>> RetrieveDevicesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ExternalDeviceDto>>([]);

    public Task<IReadOnlyList<ExternalEmployeeDto>> RetrieveEmployeesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ExternalEmployeeDto>>([]);

    /// <summary>Returns every row of <see cref="AttendanceIntegrationConfig.UploadFilePath"/>. <paramref name="range"/> is ignored: an upload is taken whole.</summary>
    public async Task<AttendanceBatchResult> RetrieveAttendanceTransactionsAsync(AttendanceIntegrationConfig config, DateRange? range, CancellationToken cancellationToken)
    {
        var path = config.UploadFilePath ?? throw new ArgumentException("The generic CSV provider reads uploaded files only; no file was given.", nameof(config));

        string content;
        using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            content = await reader.ReadToEndAsync(cancellationToken);
        }

        var rows = ParseCsv(content);
        if (rows.Count == 0)
        {
            return new AttendanceBatchResult([]);
        }

        var headers = UniqueHeaders(rows[0]);
        var transactions = rows.Skip(1).Select(row => ToTransaction(headers, row)).ToList();
        return new AttendanceBatchResult(transactions);
    }

    public Task<AttendanceBatchResult> GetTransactionChangesAsync(AttendanceIntegrationConfig config, SyncCursor? cursor, CancellationToken cancellationToken)
        => throw new NotSupportedException("The generic CSV provider is manual-upload only; it has no incremental sync.");

    private static ExternalAttendanceTransaction ToTransaction(IReadOnlyList<string> headers, IReadOnlyList<string> row)
    {
        // Every cell is kept, even beyond the header (named column{n}); a short row just has fewer.
        var payload = new Dictionary<string, string>();
        for (var i = 0; i < row.Count; i++)
        {
            payload[i < headers.Count ? headers[i] : $"column{i + 1}"] = row[i];
        }

        string? Field(string[] names)
        {
            var header = headers.FirstOrDefault(h => names.Contains(Normalize(h)));
            return header is not null && payload.TryGetValue(header, out var value) && value.Length > 0 ? value : null;
        }

        var timestampRaw = Field(TimestampColumns) ?? string.Empty;
        return new ExternalAttendanceTransaction(JsonSerializer.Serialize(payload, PayloadOptions), timestampRaw)
        {
            Timestamp = Interpreter.ParseTimestamp(timestampRaw),
            ExternalEmployeeId = Field(EmployeeColumns),
            TransactionTypeRaw = Field(TypeColumns),
            DirectionRaw = Field(DirectionColumns),
            DeviceExternalId = Field(DeviceColumns),
            ExternalTransactionId = Field(TransactionIdColumns),
        };
    }

    private static string Normalize(string header) =>
        new string(header.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static List<string> UniqueHeaders(IReadOnlyList<string> header)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(header.Count);
        for (var i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim();
            if (name.Length == 0 || !seen.Add(name))
            {
                name = $"column{i + 1}";
                seen.Add(name);
            }

            result.Add(name);
        }

        return result;
    }

    /// <summary>RFC 4180: comma-separated, double-quoted fields may hold commas, quotes ("") and line breaks. Blank lines are skipped.</summary>
    private static List<List<string>> ParseCsv(string content)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var rowHasContent = false;

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            if (rowHasContent || row.Count > 1 || row[0].Length > 0)
            {
                rows.Add(row);
            }

            row = [];
            rowHasContent = false;
        }

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < content.Length && content[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    rowHasContent = true;
                    break;
                case ',':
                    EndField();
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRow();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0 || rowHasContent)
        {
            EndRow();
        }

        return rows;
    }
}
