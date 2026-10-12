using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>Describes a custom file format as the upload screen shows a layout, from the format itself so the screen shows what the reader does.</summary>
public static class FileFormatLayout
{
    public static IReadOnlyList<string> FileExtensions(AttendanceFileKind kind) => kind switch
    {
        AttendanceFileKind.Csv => [".csv", ".txt"],
        AttendanceFileKind.Excel => [".xls", ".xlsx"],
        _ => [".csv", ".txt", ".xls", ".xlsx"],
    };

    /// <summary>Splits the stored patterns (separated by <c>;</c> or line breaks) into a list.</summary>
    public static IReadOnlyList<string> Patterns(string timestampFormats) =>
        timestampFormats.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static ProviderMappingGuide Describe(AttendanceFileFormat format, string providerKey)
    {
        var fields = new List<FieldMappingRule>
        {
            new() { Source = format.EmployeeColumn, Target = "ExternalEmployeeId", Note = "Map it to an employee under the integration's employee ids." },
        };

        if (format.TimestampColumn.Length > 0)
        {
            fields.Add(new FieldMappingRule { Source = format.TimestampColumn, Target = "TransactionTimestampRaw", Note = "Without an offset the device's time zone gives the instant." });
        }
        else
        {
            fields.Add(new FieldMappingRule { Source = $"{format.DateColumn} + {format.TimeColumn}", Target = "TransactionTimestampRaw", Note = "The date and the time, joined by a space, are read by the timestamp patterns." });
        }

        AddIfSet(fields, format.TypeColumn, "TransactionTypeRaw", "Tried before the direction column.");
        AddIfSet(fields, format.DirectionColumn, "DirectionRaw", null);
        AddIfSet(fields, format.DeviceColumn, "DeviceExternalId", "Must match a registered device's external id.");
        AddIfSet(fields, format.TransactionIdColumn, "ExternalTransactionId", null);

        var layout = format.HasHeaderRow ? "a header row, and the columns are named by their headers" : "no header row, and the columns are numbered from 1";
        return new ProviderMappingGuide
        {
            ProviderKey = providerKey,
            Summary = $"Custom format '{format.Name}': {format.FileKind} file with {layout}"
                + (format.FileKind == AttendanceFileKind.Excel ? $" (sheet {format.SheetNumber})." : $", fields separated by {DelimiterName(format.Delimiter)}.")
                + " Every column is kept in the payload; biometric-looking ones are removed by the shared sanitizer.",
            FileFormats = FileExtensions(format.FileKind),
            Fields = fields,
            TimestampFormats = Patterns(format.TimestampFormats),
            EventCodes = format.EventCodes.OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase).Select(c => new EventCodeRule { Code = c.Code, EventType = c.EventType.ToString() }).ToList(),
            NotStored = ["columns whose name contains: template, biometric, fingerprint, faceprint"],
        };
    }

    /// <summary>A label reduced to upper-case letters and digits, so <c>Check-In</c> and <c>check in</c> are the same code.</summary>
    public static string CodeKey(string? label) =>
        new string((label ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public static string DelimiterName(string delimiter) => delimiter switch
    {
        "," => "commas",
        ";" => "semicolons",
        "|" => "vertical bars",
        "\t" => "tabs",
        _ => $"'{delimiter}'",
    };

    private static void AddIfSet(List<FieldMappingRule> fields, string column, string target, string? note)
    {
        if (column.Length > 0)
        {
            fields.Add(new FieldMappingRule { Source = column, Target = target, Note = note });
        }
    }
}
