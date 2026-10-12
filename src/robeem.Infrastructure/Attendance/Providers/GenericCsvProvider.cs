using System.Text.Encodings.Web;
using System.Text.Json;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// Reads punches from an uploaded CSV file, or an Excel sheet read through <see cref="TabularFile"/>
/// (the rest of this description says CSV, and applies to the sheet's rows). **Manual upload only**: there is no connection to
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
public sealed class GenericCsvProvider : IAttendanceProvider, IAttendanceMappingGuide, IAttendanceFileReader
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

        var rows = await TabularFile.ReadRowsAsync(path, cancellationToken);
        if (rows.Count == 0)
        {
            return new AttendanceBatchResult([]);
        }

        var headers = CsvDocument.UniqueHeaders(rows[0]);
        var transactions = rows.Skip(1).Select(row => ToTransaction(headers, row)).ToList();
        return new AttendanceBatchResult(transactions);
    }

    public Task<AttendanceBatchResult> GetTransactionChangesAsync(AttendanceIntegrationConfig config, SyncCursor? cursor, CancellationToken cancellationToken)
        => throw new NotSupportedException("The generic CSV provider is manual-upload only; it has no incremental sync.");

    public ProviderMappingGuide DescribeMapping() => new()
    {
        ProviderKey = ProviderKey,
        FileFormats = [".csv", ".xls", ".xlsx"],
        Summary = "A CSV file, or an Excel sheet (XLS or XLSX, first sheet), with a header row, uploaded by hand. Columns are matched by name, ignoring case, spaces and punctuation. Every column is kept in the payload; biometric-looking ones are removed by the shared sanitizer.",
        Fields =
        [
            Rule(EmployeeColumns, "ExternalEmployeeId"),
            Rule(TimestampColumns, "TransactionTimestampRaw", "Parsed by the interpreter; without an offset the device's time zone gives the instant."),
            Rule(TypeColumns, "TransactionTypeRaw", "Tried before the direction column."),
            Rule(DirectionColumns, "DirectionRaw"),
            Rule(DeviceColumns, "DeviceExternalId", "Must match a registered device's external id."),
            Rule(TransactionIdColumns, "ExternalTransactionId"),
        ],
        TimestampFormats = GenericCsvInterpreter.TimestampFormats.Select(f => f.Replace("'", string.Empty)).ToList(),
        EventCodes = GenericCsvInterpreter.RecognizedEventTypes.Select(e => new EventCodeRule { Code = e.Key, EventType = e.Value.ToString() }).ToList(),
        NotStored = ["columns whose name contains: template, biometric, fingerprint, faceprint"],
    };

    private static FieldMappingRule Rule(string[] columns, string target, string? note = null) =>
        new() { Source = string.Join(" / ", columns), Target = target, Note = note };

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
            var header = headers.FirstOrDefault(h => names.Contains(CsvDocument.NormalizeHeader(h)));
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
}
