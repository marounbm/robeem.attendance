using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// Reads an uploaded file by an <see cref="AttendancePayloadFormat"/>: text split by its delimiter or a sheet of a
/// workbook, with or without a header row. Each data row becomes one transaction whose raw payload is the whole
/// row (header, or <c>column{n}</c>, to text), or only the format's allowlisted columns; the mapped columns also
/// fill the transaction's fields. A column setting may list several headers: the first non-empty value is used. The
/// timestamp is parsed by the format's own interpreter, as the generic CSV adapter does.
/// </summary>
public sealed class CustomFormatFileReader : ICustomFormatFileReader
{
    private static readonly JsonSerializerOptions PayloadOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<IReadOnlyList<ExternalAttendanceTransaction>> ReadAsync(AttendancePayloadFormat format, string path, CancellationToken cancellationToken)
    {
        var delimiter = format.Delimiter.Length == 1 ? format.Delimiter[0] : ',';
        var table = await TabularFile.ReadTableAsync(path, format.PayloadKind, delimiter, format.SheetNumber, format.RecordPath, cancellationToken);
        var rows = table.Rows;
        if (rows.Count == 0)
        {
            return [];
        }

        // JSON and XML records are read into a table whose first row names the fields, whatever the format says about header rows.
        var hasHeaderRow = format.HasHeaderRow || format.PayloadKind is AttendancePayloadKind.Json or AttendancePayloadKind.Xml;
        List<string> headers;
        int firstDataRow;
        if (hasHeaderRow)
        {
            headers = CsvDocument.UniqueHeaders(rows[0]);
            firstDataRow = 1;
        }
        else
        {
            headers = Enumerable.Range(1, rows.Max(r => r.Count)).Select(n => $"column{n}").ToList();
            firstDataRow = 0;
        }

        // With a date pattern and a time pattern of their own, a workbook's real date and time cells are not read through the text
        // they display: the value is written out in the format's first pattern, which the interpreter then reads back.
        var separate = PayloadFormatLayout.UsesSeparatePatterns(format);
        var firstDatePattern = PayloadFormatLayout.Patterns(format.DateFormats).FirstOrDefault() ?? string.Empty;
        var firstTimePattern = PayloadFormatLayout.Patterns(format.TimeFormats).FirstOrDefault() ?? string.Empty;
        var culture = TimestampCultures.Resolve(format.TimestampCulture);

        var interpreter = new CustomFormatInterpreter(format);
        var employee = Locate(format.EmployeeColumn, headers, hasHeaderRow, nameof(format.EmployeeColumn));
        var timestamp = Locate(format.TimestampColumn, headers, hasHeaderRow, nameof(format.TimestampColumn));
        var date = Locate(format.DateColumn, headers, hasHeaderRow, nameof(format.DateColumn));
        var time = Locate(format.TimeColumn, headers, hasHeaderRow, nameof(format.TimeColumn));
        var type = Locate(format.TypeColumn, headers, hasHeaderRow, nameof(format.TypeColumn));
        var direction = Locate(format.DirectionColumn, headers, hasHeaderRow, nameof(format.DirectionColumn));
        var device = Locate(format.DeviceColumn, headers, hasHeaderRow, nameof(format.DeviceColumn));
        var transactionId = Locate(format.TransactionIdColumn, headers, hasHeaderRow, nameof(format.TransactionIdColumn));

        // With a list of columns to keep, only those enter the payload, under the name the format gives them (CLAUDE.md §1.10).
        var kept = hasHeaderRow
            ? PayloadFormatLayout.Aliases(format.PayloadColumns).GroupBy(CsvDocument.NormalizeHeader).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
            : [];

        var transactions = new List<ExternalAttendanceTransaction>();
        for (var rowIndex = firstDataRow; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var payload = new Dictionary<string, string>();
            if (kept.Count > 0)
            {
                // A short row just has fewer.
                for (var i = 0; i < row.Count && i < headers.Count; i++)
                {
                    if (kept.TryGetValue(CsvDocument.NormalizeHeader(headers[i]), out var name))
                    {
                        payload[name] = row[i];
                    }
                }
            }
            else
            {
                // Every cell is kept, even beyond the headers (named column{n}); a short row just has fewer.
                for (var i = 0; i < row.Count; i++)
                {
                    payload[i < headers.Count ? headers[i] : $"column{i + 1}"] = row[i];
                }
            }

            int? Pick(IReadOnlyList<int> indexes)
            {
                // The first non-empty value among the columns the format lists.
                foreach (var at in indexes)
                {
                    if (at < row.Count && row[at].Trim().Length > 0)
                    {
                        return at;
                    }
                }

                return null;
            }

            string? Cell(IReadOnlyList<int> indexes) => Pick(indexes) is { } at ? row[at].Trim() : null;

            string? DatePart(IReadOnlyList<int> indexes, bool isDate)
            {
                if (Pick(indexes) is not { } at)
                {
                    return null;
                }

                if (separate && table.DateAt(rowIndex, at) is { } value)
                {
                    return isDate
                        ? value.Date.ToString(firstDatePattern, culture)
                        : DateTime.MinValue.Add(value.TimeOfDay).ToString(firstTimePattern, culture);
                }

                return row[at].Trim();
            }

            var timestampText = format.TimestampColumn.Length > 0
                ? Cell(timestamp) ?? string.Empty
                : string.Join(' ', new[] { DatePart(date, isDate: true), DatePart(time, isDate: false) }.Where(part => part is not null));
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

    /// <summary>
    /// The indexes of a mapped column, in the order the format lists its headers: none when nothing is mapped. A single
    /// header the file does not have throws; of several listed headers the file may have any number, even none.
    /// </summary>
    private static IReadOnlyList<int> Locate(string column, IReadOnlyList<string> headers, bool hasHeaderRow, string field)
    {
        if (column.Length == 0)
        {
            return [];
        }

        if (!hasHeaderRow)
        {
            var number = int.Parse(column, CultureInfo.InvariantCulture);
            return number <= headers.Count ? [number - 1] : throw new InvalidDataException($"The file has no column {number} ({field}).");
        }

        var names = PayloadFormatLayout.Aliases(column);
        var indexes = new List<int>();
        foreach (var name in names)
        {
            var wanted = CsvDocument.NormalizeHeader(name);
            var index = headers.ToList().FindIndex(h => CsvDocument.NormalizeHeader(h) == wanted);
            if (index >= 0 && !indexes.Contains(index))
            {
                indexes.Add(index);
            }
        }

        return indexes.Count == 0 && names.Count == 1 ? throw new InvalidDataException($"The file has no column named '{names[0]}' ({field}).") : indexes;
    }
}
