using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>Describes a custom file format as the upload screen shows a layout, from the format itself so the screen shows what the reader does.</summary>
public static class PayloadFormatLayout
{
    /// <summary>The most date patterns times time patterns a format may combine.</summary>
    public const int MaxPatternCombinations = 100;

    public static IReadOnlyList<string> FileExtensions(AttendancePayloadKind kind) => kind switch
    {
        AttendancePayloadKind.Csv => [".csv", ".txt"],
        AttendancePayloadKind.Excel => [".xls", ".xlsx"],
        AttendancePayloadKind.Json => [".json"],
        AttendancePayloadKind.Xml => [".xml"],
        _ => [".csv", ".txt", ".xls", ".xlsx"],
    };

    /// <summary>Splits the stored patterns (separated by <c>;</c> or line breaks) into a list.</summary>
    public static IReadOnlyList<string> Patterns(string timestampFormats) =>
        timestampFormats.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Gets a value indicating whether the format reads a date column and a time column by patterns of their own.</summary>
    public static bool UsesSeparatePatterns(AttendancePayloadFormat format) =>
        format.TimestampColumn.Length == 0 && Patterns(format.DateFormats).Count > 0 && Patterns(format.TimeFormats).Count > 0;

    /// <summary>
    /// The patterns the timestamp text is tried against, in the order they win: the format's own list, or for separate date and
    /// time patterns every date pattern followed by a space and each time pattern. The text stored with a punch is always the
    /// date and the time joined by a space, so it reads back by these patterns whatever changes later.
    /// </summary>
    public static IReadOnlyList<string> EffectivePatterns(AttendancePayloadFormat format) =>
        UsesSeparatePatterns(format)
            ? Patterns(format.DateFormats).SelectMany(date => Patterns(format.TimeFormats).Select(time => date + " " + time)).ToList()
            : Patterns(format.TimestampFormats);

    /// <summary>Splits a column setting (headers separated by <c>|</c>) into its headers, in order.</summary>
    public static IReadOnlyList<string> Aliases(string column) =>
        column.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static VendorMappingGuide Describe(AttendancePayloadFormat format, string vendorKey)
    {
        var fields = new List<FieldMappingRule>
        {
            new() { Source = AliasText(format.EmployeeColumn), Target = "ExternalEmployeeId", Note = "Map it to an employee under the integration's employee ids." },
        };

        if (format.TimestampColumn.Length > 0)
        {
            fields.Add(new FieldMappingRule { Source = AliasText(format.TimestampColumn), Target = "TransactionTimestampRaw", Note = "Without an offset the device's time zone gives the instant." });
        }
        else
        {
            var note = UsesSeparatePatterns(format)
                ? "The date is read by the date patterns and the time by the time patterns, joined by a space."
                : "The date and the time, joined by a space, are read by the timestamp patterns.";
            fields.Add(new FieldMappingRule { Source = $"{AliasText(format.DateColumn)} + {AliasText(format.TimeColumn)}", Target = "TransactionTimestampRaw", Note = note });
        }

        AddIfSet(fields, AliasText(format.TypeColumn), "TransactionTypeRaw", "Tried before the direction column.");
        AddIfSet(fields, AliasText(format.DirectionColumn), "DirectionRaw", null);
        AddIfSet(fields, AliasText(format.DeviceColumn), "DeviceExternalId", "Must match a registered device's external id.");
        AddIfSet(fields, AliasText(format.TransactionIdColumn), "ExternalTransactionId", null);

        var keptColumns = Aliases(format.PayloadColumns);
        var structured = format.PayloadKind is AttendancePayloadKind.Json or AttendancePayloadKind.Xml;
        var layout = structured
            ? "fields named by their path inside each record"
            : format.HasHeaderRow ? "a header row, and the columns are named by their headers" : "no header row, and the columns are numbered from 1";
        var where = format.PayloadKind switch
        {
            AttendancePayloadKind.Excel => $" (sheet {format.SheetNumber}).",
            AttendancePayloadKind.Json or AttendancePayloadKind.Xml => format.RecordPath.Length > 0 ? $", records at '{format.RecordPath}'." : ", records at the top level.",
            _ => $", fields separated by {DelimiterName(format.Delimiter)}.",
        };
        return new VendorMappingGuide
        {
            VendorKey = vendorKey,
            Summary = $"Custom format '{format.Name}': {format.PayloadKind} file with {layout}"
                + where
                + (keptColumns.Count == 0
                    ? " Every column is kept in the payload; biometric-looking ones are removed by the shared sanitizer."
                    : $" Only these columns are kept in the payload: {string.Join(", ", keptColumns)}."),
            FileExtensions = FileExtensions(format.PayloadKind),
            Fields = fields,
            TimestampFormats = EffectivePatterns(format),
            EventCodes = format.EventCodes.OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase).Select(c => new EventCodeRule { Code = c.Code, EventType = c.EventType.ToString() }).ToList(),
            NotStored = keptColumns.Count == 0
                ? ["columns whose name contains: template, biometric, fingerprint, faceprint"]
                : ["any column not listed in the payload", "columns whose name contains: template, biometric, fingerprint, faceprint"],
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

    private static string AliasText(string column) => string.Join(" / ", Aliases(column));

    private static void AddIfSet(List<FieldMappingRule> fields, string column, string target, string? note)
    {
        if (column.Length > 0)
        {
            fields.Add(new FieldMappingRule { Source = column, Target = target, Note = note });
        }
    }
}
