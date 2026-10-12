using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// Reads an uploaded file by an <see cref="AttendanceFileFormat"/>: text split by its delimiter or a sheet of a
/// workbook, with or without a header row. Each data row becomes one transaction whose raw payload is the whole
/// row (header, or <c>column{n}</c>, to text); the mapped columns also fill the transaction's fields. The
/// timestamp is parsed by the format's own interpreter, as the generic CSV adapter does.
/// </summary>
public sealed class CustomFormatFileReader : ICustomFormatFileReader
{
    private static readonly JsonSerializerOptions PayloadOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<IReadOnlyList<ExternalAttendanceTransaction>> ReadAsync(AttendanceFileFormat format, string path, CancellationToken cancellationToken)
    {
        var delimiter = format.Delimiter.Length == 1 ? format.Delimiter[0] : ',';
        var rows = await TabularFile.ReadRowsAsync(path, format.FileKind, delimiter, format.SheetNumber, cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        List<string> headers;
        IEnumerable<List<string>> data;
        if (format.HasHeaderRow)
        {
            headers = CsvDocument.UniqueHeaders(rows[0]);
            data = rows.Skip(1);
        }
        else
        {
            headers = Enumerable.Range(1, rows.Max(r => r.Count)).Select(n => $"column{n}").ToList();
            data = rows;
        }

        var interpreter = new CustomFormatInterpreter(format);
        var employee = Locate(format.EmployeeColumn, headers, format.HasHeaderRow, nameof(format.EmployeeColumn));
        var timestamp = Locate(format.TimestampColumn, headers, format.HasHeaderRow, nameof(format.TimestampColumn));
        var date = Locate(format.DateColumn, headers, format.HasHeaderRow, nameof(format.DateColumn));
        var time = Locate(format.TimeColumn, headers, format.HasHeaderRow, nameof(format.TimeColumn));
        var type = Locate(format.TypeColumn, headers, format.HasHeaderRow, nameof(format.TypeColumn));
        var direction = Locate(format.DirectionColumn, headers, format.HasHeaderRow, nameof(format.DirectionColumn));
        var device = Locate(format.DeviceColumn, headers, format.HasHeaderRow, nameof(format.DeviceColumn));
        var transactionId = Locate(format.TransactionIdColumn, headers, format.HasHeaderRow, nameof(format.TransactionIdColumn));

        var transactions = new List<ExternalAttendanceTransaction>();
        foreach (var row in data)
        {
            // Every cell is kept, even beyond the headers (named column{n}); a short row just has fewer.
            var payload = new Dictionary<string, string>();
            for (var i = 0; i < row.Count; i++)
            {
                payload[i < headers.Count ? headers[i] : $"column{i + 1}"] = row[i];
            }

            string? Cell(int? index) => index is { } at && at < row.Count && row[at].Trim().Length > 0 ? row[at].Trim() : null;

            var timestampText = timestamp is not null
                ? Cell(timestamp) ?? string.Empty
                : string.Join(' ', new[] { Cell(date), Cell(time) }.Where(part => part is not null));
            transactions.Add(new ExternalAttendanceTransaction(JsonSerializer.Serialize(payload, PayloadOptions), timestampText)
            {
                Timestamp = interpreter.ParseTimestamp(timestampText),
                ExternalEmployeeId = Cell(employee),
                TransactionTypeRaw = Cell(type),
                DirectionRaw = Cell(direction),
                DeviceExternalId = Cell(device),
                ExternalTransactionId = Cell(transactionId),
            });
        }

        return transactions;
    }

    /// <summary>The index of a mapped column: null when none is mapped; throws when the file does not have it.</summary>
    private static int? Locate(string column, IReadOnlyList<string> headers, bool hasHeaderRow, string field)
    {
        if (column.Length == 0)
        {
            return null;
        }

        if (!hasHeaderRow)
        {
            var number = int.Parse(column, CultureInfo.InvariantCulture);
            return number <= headers.Count ? number - 1 : throw new InvalidDataException($"The file has no column {number} ({field}).");
        }

        var wanted = CsvDocument.NormalizeHeader(column);
        var index = headers.ToList().FindIndex(h => CsvDocument.NormalizeHeader(h) == wanted);
        return index >= 0 ? index : throw new InvalidDataException($"The file has no column named '{column}' ({field}).");
    }
}
