using System.Text.Encodings.Web;
using System.Text.Json;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// Reads punches from a ZKTeco attendance export, as the software wrote it (<c>.xls</c>) or saved as
/// CSV or <c>.xlsx</c>; see <see cref="TabularFile"/>. Upload only: the device-pull path
/// (SDK or ADMS push) is not built, so there is no connection to validate, no incremental sync, and
/// <see cref="GetTransactionChangesAsync"/> throws <see cref="NotSupportedException"/>.
/// </summary>
/// <remarks>
/// <para>The export has the columns Department, Name, No., Date/Time, Status, Location ID, ID Number,
/// VerifyCode and CardNo. Upload the file as exported; there is no need to re-save it as CSV. Each
/// row becomes one transaction. The raw payload is built from an <b>allowlist</b> of columns
/// (<see cref="PayloadColumns"/>); any other column, including CardNo, is never copied
/// (CLAUDE.md §1.10). Header matching ignores case, spaces and punctuation.</para>
/// <para>Timestamp and status formats are described on <see cref="ZktecoInterpreter"/>.</para>
/// </remarks>
public sealed class ZktecoProvider : IAttendanceProvider, IAttendanceMappingGuide, IAttendanceFileReader
{
    public const string ProviderKey = "zkteco";

    private const string PullNotBuilt = "The ZKTeco adapter reads exported files only (XLS, XLSX or CSV); pulling from the device is not available.";

    private static readonly ZktecoInterpreter Interpreter = new();

    private static readonly JsonSerializerOptions PayloadOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Normalized header (see CsvDocument.NormalizeHeader) to the vendor's own column name, which is the payload key.
    private static readonly Dictionary<string, string> PayloadColumns = new(StringComparer.Ordinal)
    {
        ["department"] = "Department",
        ["name"] = "Name",
        ["no"] = "No.",
        ["datetime"] = "Date/Time",
        ["status"] = "Status",
        ["locationid"] = "Location ID",
        ["idnumber"] = "ID Number",
        ["verifycode"] = "VerifyCode",
    };

    public Task<ConnectionValidationResult> ValidateConnectionAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult(new ConnectionValidationResult(true, "File upload provider: there is no connection to validate."));

    public Task<AuthResult> AuthenticateAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult(new AuthResult(true));

    public Task<IReadOnlyList<ExternalDeviceDto>> RetrieveDevicesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ExternalDeviceDto>>([]);

    public Task<IReadOnlyList<ExternalEmployeeDto>> RetrieveEmployeesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ExternalEmployeeDto>>([]);

    /// <summary>Returns every row of <see cref="AttendanceIntegrationConfig.UploadFilePath"/>. <paramref name="range"/> is ignored: an upload is taken whole.</summary>
    public async Task<AttendanceBatchResult> RetrieveAttendanceTransactionsAsync(AttendanceIntegrationConfig config, DateRange? range, CancellationToken cancellationToken)
    {
        var path = config.UploadFilePath ?? throw new ArgumentException("The ZKTeco adapter reads uploaded files only; no file was given.", nameof(config));

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
        => throw new NotSupportedException(PullNotBuilt);

    public ProviderMappingGuide DescribeMapping() => new()
    {
        ProviderKey = ProviderKey,
        FileFormats = [".xls", ".xlsx", ".csv"],
        Summary = "The attendance export of the ZKTeco clock's own software (Fingerprint Attendance System), uploaded as exported (XLS; XLSX or CSV also work). One row per punch with the nine columns below, in this order and with these headers. Times are wall-clock at the device (no offset), day first. Break Out, Break In, OT In and OT Out are also read when the clock writes them; any other Status fails the row.",
        Fields =
        [
            new FieldMappingRule { Source = "Department", Target = "payload only", Note = "e.g. 2 - Sales" },
            new FieldMappingRule { Source = "Name", Target = "payload only", Note = "e.g. James Carter" },
            new FieldMappingRule { Source = "No.", Target = "ExternalEmployeeId", Note = "e.g. 22001. Falls back to ID Number when empty." },
            new FieldMappingRule { Source = "Date/Time", Target = "TransactionTimestampRaw", Note = "e.g. 01-Apr-26 08:33:25. The device's time zone gives the instant." },
            new FieldMappingRule { Source = "Status", Target = "TransactionTypeRaw", Note = "e.g. C/In or C/Out" },
            new FieldMappingRule { Source = "Location ID", Target = "DeviceExternalId", Note = "e.g. 1. Must match a registered device's external id." },
            new FieldMappingRule { Source = "ID Number", Target = "ExternalEmployeeId", Note = "e.g. 22001. Used only when No. is empty." },
            new FieldMappingRule { Source = "VerifyCode", Target = "payload only", Note = "e.g. Fingerpint (sic). The verification method label, not a template." },
            new FieldMappingRule { Source = "CardNo", Target = "not stored", Note = "Usually empty; never copied." },
        ],
        TimestampFormats = [ZktecoInterpreter.ExportTimestampFormat],
        EventCodes = ZktecoInterpreter.EventCodes
            .Where(c => ZktecoInterpreter.ExportEventCodes.Contains(c.Code))
            .Select(c => new EventCodeRule { Code = c.Code, EventType = c.Type.ToString() })
            .ToList(),
        NotStored = ["CardNo", "any column not listed above"],
    };

    private static ExternalAttendanceTransaction ToTransaction(IReadOnlyList<string> headers, IReadOnlyList<string> row)
    {
        // Only allowlisted columns are kept, under the vendor's own names. A short row just has fewer.
        var payload = new Dictionary<string, string>();
        for (var i = 0; i < row.Count && i < headers.Count; i++)
        {
            if (PayloadColumns.TryGetValue(CsvDocument.NormalizeHeader(headers[i]), out var column))
            {
                payload[column] = row[i];
            }
        }

        string? Field(string column) => payload.TryGetValue(column, out var value) && value.Length > 0 ? value : null;

        var timestampRaw = Field("Date/Time") ?? string.Empty;
        return new ExternalAttendanceTransaction(JsonSerializer.Serialize(payload, PayloadOptions), timestampRaw)
        {
            Timestamp = Interpreter.ParseTimestamp(timestampRaw),
            ExternalEmployeeId = Field("No.") ?? Field("ID Number"),
            TransactionTypeRaw = Field("Status"),
            DeviceExternalId = Field("Location ID"),
        };
    }
}
