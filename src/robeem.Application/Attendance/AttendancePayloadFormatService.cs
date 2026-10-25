using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendancePayloadFormatService : IAttendancePayloadFormatService
{
    private const int PreviewRows = 20;
    private const int MaxEventCodes = 50;
    private const int MaxPatternsPerList = 10;

    private static readonly string[] AllowedDelimiters = [",", ";", "|", "\t"];

    // A column that can only hold a biometric template is never read, whatever the file calls it (CLAUDE.md §1.10).
    private static readonly string[] BiometricWords = ["template", "biometric", "fingerprint", "faceprint"];

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;
    private readonly ICustomFormatFileReader _reader;
    private readonly ICustomFormatInterpreterFactory _interpreters;

    public AttendancePayloadFormatService(IRobeemDbContext db, IAttendanceAuditTrail audit, ICustomFormatFileReader reader, ICustomFormatInterpreterFactory interpreters)
    {
        _db = db;
        _audit = audit;
        _reader = reader;
        _interpreters = interpreters;
    }

    /// <summary>
    /// A warning for each pair of patterns that differ only in the order of day and month (<c>dd/MM/yyyy</c> and <c>MM/dd/yyyy</c>): a text such as
    /// 03/04/2026 fits both, and the pattern listed first wins. Not an error: a file may genuinely need both.
    /// </summary>
    public static IReadOnlyList<string> AmbiguityWarnings(IReadOnlyList<string> patterns)
    {
        var warnings = new List<string>();
        foreach (var pattern in patterns)
        {
            if (pattern.Contains("MMM", StringComparison.Ordinal))
            {
                continue;
            }

            var swapped = new string(pattern.Select(c => c == 'd' ? 'M' : c == 'M' ? 'd' : c).ToArray());
            var at = patterns.ToList().IndexOf(pattern);
            var other = patterns.ToList().IndexOf(swapped);
            if (swapped != pattern && other > at)
            {
                warnings.Add($"'{pattern}' and '{swapped}' read the same text differently when the day is 12 or less (such as 03/04/2026): '{pattern}' is listed first, so it wins.");
            }
        }

        return warnings;
    }

    public async Task<IReadOnlyList<PayloadFormatListItem>> ListAsync(CancellationToken cancellationToken)
    {
        // Only the latest version of each format is listed; the older ones stay for the punches they read.
        var rows = await _db.AttendancePayloadFormats.AsNoTracking()
            .Where(f => !_db.AttendancePayloadFormats.Any(newer => newer.SupersedesId == f.Id))
            .OrderBy(f => f.Name)
            .Select(f => new
            {
                f.Id,
                f.Version,
                f.Name,
                f.Description,
                f.PayloadKind,
                f.Delimiter,
                f.IsActive,
                EventCodeCount = f.EventCodes.Count,
                IntegrationCount = _db.AttendanceIntegrations.Count(i => i.PayloadFormatId == f.Id),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(f => new PayloadFormatListItem
        {
            Id = f.Id,
            Version = f.Version,
            Name = f.Name,
            Description = f.Description,
            PayloadKind = f.PayloadKind.ToString(),
            Delimiter = PayloadFormatLayout.DelimiterName(f.Delimiter),
            IsActive = f.IsActive,
            EventCodeCount = f.EventCodeCount,
            IntegrationCount = f.IntegrationCount,
        }).ToList();
    }

    public async Task<IReadOnlyList<PayloadFormatOption>> ListOptionsAsync(CancellationToken cancellationToken) =>
        await _db.AttendancePayloadFormats.AsNoTracking()
            .Where(f => f.IsActive && !_db.AttendancePayloadFormats.Any(newer => newer.SupersedesId == f.Id))
            .OrderBy(f => f.Name)
            .Select(f => new PayloadFormatOption { Id = f.Id, Name = f.Name })
            .ToListAsync(cancellationToken);

    public async Task<PayloadFormatInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        var format = await _db.AttendancePayloadFormats.AsNoTracking().Include(f => f.EventCodes).SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        return format is null
            ? null
            : new PayloadFormatInput
            {
                Name = format.Name,
                Description = format.Description,
                PayloadKind = format.PayloadKind.ToString(),
                Delimiter = format.Delimiter,
                HasHeaderRow = format.HasHeaderRow,
                RecordPath = format.RecordPath,
                SheetNumber = format.SheetNumber,
                IsActive = format.IsActive,
                EmployeeColumn = format.EmployeeColumn,
                TimestampColumn = format.TimestampColumn,
                DateColumn = format.DateColumn,
                TimeColumn = format.TimeColumn,
                TypeColumn = format.TypeColumn,
                DirectionColumn = format.DirectionColumn,
                DeviceColumn = format.DeviceColumn,
                TransactionIdColumn = format.TransactionIdColumn,
                PayloadColumns = format.PayloadColumns,
                TimestampFormats = string.Join(Environment.NewLine, PayloadFormatLayout.Patterns(format.TimestampFormats)),
                DateFormats = string.Join(Environment.NewLine, PayloadFormatLayout.Patterns(format.DateFormats)),
                TimeFormats = string.Join(Environment.NewLine, PayloadFormatLayout.Patterns(format.TimeFormats)),
                TimestampCulture = format.TimestampCulture,
                EventCodes = format.EventCodes.OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase).Select(c => new PayloadFormatEventCodeInput { Code = c.Code, EventType = c.EventType.ToString() }).ToList(),
            };
    }

    public async Task<SaveResult> CreateAsync(PayloadFormatInput input, CancellationToken cancellationToken)
    {
        var errors = Validate(input);
        await CheckNameAsync(input, null, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var format = new AttendancePayloadFormat();
        Apply(input, format);
        foreach (var code in CleanCodes(input))
        {
            format.EventCodes.Add(code);
        }

        _db.AttendancePayloadFormats.Add(format);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendancePayloadFormat), format.Id, $"File format '{format.Name}' created: {Summary(format)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(format.Id);
    }

    public async Task<SaveResult> UpdateAsync(int id, PayloadFormatInput input, CancellationToken cancellationToken)
    {
        var format = await _db.AttendancePayloadFormats.Include(f => f.EventCodes).SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (format is null)
        {
            return SaveResult.Missing();
        }

        if (await _db.AttendancePayloadFormats.AnyAsync(f => f.SupersedesId == id, cancellationToken))
        {
            return SaveResult.Invalid("id", "A newer version of this format exists. Edit that one: this version is kept only for the punches it read.");
        }

        var errors = Validate(input);
        await CheckNameAsync(input, id, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = Summary(format);

        // A change to how a file is read, made after punches were read by this format, is a new version: a punch that is
        // normalized again later is read the way it was first read, not the way the format reads now. Anything else (a name,
        // a description, switching it off) and every change before the first use is made in place.
        var candidate = new AttendancePayloadFormat();
        Apply(input, candidate);
        foreach (var code in CleanCodes(input))
        {
            candidate.EventCodes.Add(code);
        }

        if (ReadingSignature(format) != ReadingSignature(candidate)
            && await _db.AttendanceProcessingBatches.AnyAsync(b => b.PayloadFormatId == id, cancellationToken))
        {
            return await CreateVersionAsync(format, input, before, cancellationToken);
        }

        Apply(input, format);

        // Codes are matched by their normalized label: a kept one is updated in place, a new one added, a dropped one removed.
        var wanted = CleanCodes(input).ToDictionary(c => PayloadFormatLayout.CodeKey(c.Code));
        foreach (var existing in format.EventCodes.ToList())
        {
            if (wanted.Remove(PayloadFormatLayout.CodeKey(existing.Code), out var replacement))
            {
                existing.Code = replacement.Code;
                existing.EventType = replacement.EventType;
            }
            else
            {
                _db.AttendancePayloadEventCodes.Remove(existing);
            }
        }

        foreach (var added in wanted.Values)
        {
            format.EventCodes.Add(added);
        }

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendancePayloadFormat), format.Id, $"File format '{format.Name}' changed. Before: {before}. After: {Summary(format)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(format.Id);
    }

    public async Task<PayloadFormatPreview> PreviewAsync(PayloadFormatInput input, string filePath, CancellationToken cancellationToken)
    {
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return new PayloadFormatPreview { Problem = "The format is not valid yet: " + string.Join(" ", errors.Values) };
        }

        // The format is built in memory and never added to the context: nothing is stored.
        var format = new AttendancePayloadFormat();
        Apply(input, format);
        foreach (var code in CleanCodes(input))
        {
            format.EventCodes.Add(code);
        }

        IReadOnlyList<ExternalAttendanceTransaction> transactions;
        try
        {
            transactions = await _reader.ReadAsync(format, filePath, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            return new PayloadFormatPreview { Problem = ex.Message };
        }

        var interpreter = _interpreters.Create(format);
        var rows = transactions.Take(PreviewRows).Select((t, index) => ToPreviewRow(index + 1, t, interpreter)).ToList();
        return new PayloadFormatPreview { TotalRows = transactions.Count, Rows = rows, Warnings = AmbiguityWarnings(PayloadFormatLayout.UsesSeparatePatterns(format) ? PayloadFormatLayout.Patterns(format.DateFormats) : PayloadFormatLayout.Patterns(format.TimestampFormats)) };
    }

    private static PayloadFormatPreviewRow ToPreviewRow(int number, ExternalAttendanceTransaction transaction, IAttendanceEventInterpreter interpreter)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(transaction.ExternalEmployeeId))
        {
            problems.Add("no employee id");
        }

        var timestamp = transaction.TransactionTimestampRaw.Length == 0 ? null : interpreter.ParseTimestamp(transaction.TransactionTimestampRaw);
        if (timestamp is null)
        {
            problems.Add(transaction.TransactionTimestampRaw.Length == 0 ? "no timestamp" : "the timestamp matches none of the patterns");
        }

        NormalizedEventType? eventType = null;
        var label = !string.IsNullOrWhiteSpace(transaction.TransactionTypeRaw) ? transaction.TransactionTypeRaw : transaction.DirectionRaw;
        if (!string.IsNullOrWhiteSpace(label))
        {
            eventType = interpreter.MapEventType(transaction.TransactionTypeRaw, transaction.DirectionRaw);
            if (eventType is null)
            {
                problems.Add($"the label '{label}' is not mapped");
            }
        }

        return new PayloadFormatPreviewRow
        {
            RowNumber = number,
            ExternalEmployeeId = transaction.ExternalEmployeeId,
            TimestampText = transaction.TransactionTimestampRaw,
            PatternMatches = transaction.TransactionTimestampRaw.Length > 0 && interpreter is ITimestampExplainer explainer
                ? explainer.Explain(transaction.TransactionTimestampRaw).Select(m => $"{m.Pattern} → {m.Reading}").ToList()
                : [],
            ParsedTimestamp = timestamp?.WallClock.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + (timestamp?.Offset is { } offset ? " " + (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm", CultureInfo.InvariantCulture) : string.Empty),
            TypeText = label,
            EventType = eventType?.ToString(),
            DeviceText = transaction.DeviceExternalId,
            Problem = string.Join("; ", problems),
        };
    }

    private static string Summary(AttendancePayloadFormat format) =>
        $"{format.PayloadKind}, delimiter {PayloadFormatLayout.DelimiterName(format.Delimiter)}, header {(format.HasHeaderRow ? "yes" : "no")}, employee '{format.EmployeeColumn}', timestamp '{(format.TimestampColumn.Length > 0 ? format.TimestampColumn : format.DateColumn + "+" + format.TimeColumn)}', patterns '{(PayloadFormatLayout.UsesSeparatePatterns(format) ? "date " + format.DateFormats + " time " + format.TimeFormats : format.TimestampFormats)}', culture '{(format.TimestampCulture.Length > 0 ? format.TimestampCulture : "invariant")}', {format.EventCodes.Count} codes, {(format.IsActive ? "active" : "inactive")}";

    /// <summary>Everything that decides how a file is read, in a normalized form: two formats with the same signature read every file alike.</summary>
    private static string ReadingSignature(AttendancePayloadFormat format) => string.Join(
        '\u001f',
        format.PayloadKind,
        format.Delimiter,
        format.HasHeaderRow,
        format.SheetNumber,
        format.RecordPath.Trim(),
        string.Join('|', PayloadFormatLayout.Aliases(format.EmployeeColumn)),
        string.Join('|', PayloadFormatLayout.Aliases(format.TimestampColumn)),
        string.Join('|', PayloadFormatLayout.Aliases(format.DateColumn)),
        string.Join('|', PayloadFormatLayout.Aliases(format.TimeColumn)),
        string.Join('|', PayloadFormatLayout.Aliases(format.TypeColumn)),
        string.Join('|', PayloadFormatLayout.Aliases(format.DirectionColumn)),
        string.Join('|', PayloadFormatLayout.Aliases(format.DeviceColumn)),
        string.Join('|', PayloadFormatLayout.Aliases(format.TransactionIdColumn)),
        string.Join('|', PayloadFormatLayout.Aliases(format.PayloadColumns)),
        string.Join(';', PayloadFormatLayout.Patterns(format.TimestampFormats)),
        string.Join(';', PayloadFormatLayout.Patterns(format.DateFormats)),
        string.Join(';', PayloadFormatLayout.Patterns(format.TimeFormats)),
        format.TimestampCulture.Trim(),
        string.Join(';', format.EventCodes.Select(c => PayloadFormatLayout.CodeKey(c.Code) + "=" + c.EventType).Order(StringComparer.Ordinal)));

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    /// <summary>A column setting as it is stored: its headers, trimmed, separated by <c>|</c>, none empty.</summary>
    private static string Headers(string? value) => string.Join("|", PayloadFormatLayout.Aliases(Clean(value)));

    private static void Apply(PayloadFormatInput input, AttendancePayloadFormat format)
    {
        format.Name = Clean(input.Name);
        format.Description = Clean(input.Description);
        format.PayloadKind = Enum.Parse<AttendancePayloadKind>(input.PayloadKind, ignoreCase: true);
        format.Delimiter = input.Delimiter;
        format.HasHeaderRow = input.HasHeaderRow || format.PayloadKind is AttendancePayloadKind.Json or AttendancePayloadKind.Xml;
        format.RecordPath = format.PayloadKind is AttendancePayloadKind.Json or AttendancePayloadKind.Xml ? Clean(input.RecordPath) : string.Empty;
        format.SheetNumber = input.SheetNumber;
        format.IsActive = input.IsActive;
        format.EmployeeColumn = Headers(input.EmployeeColumn);
        format.TimestampColumn = Headers(input.TimestampColumn);
        format.DateColumn = Headers(input.DateColumn);
        format.TimeColumn = Headers(input.TimeColumn);
        format.TypeColumn = Headers(input.TypeColumn);
        format.DirectionColumn = Headers(input.DirectionColumn);
        format.DeviceColumn = Headers(input.DeviceColumn);
        format.TransactionIdColumn = Headers(input.TransactionIdColumn);
        format.PayloadColumns = Headers(input.PayloadColumns);
        format.TimestampCulture = Clean(input.TimestampCulture);

        // One timestamp column is read by its own patterns; a date and a time column by the date and time patterns when both
        // are given, otherwise (a format from before they existed) by patterns that describe the joined text.
        var dates = PayloadFormatLayout.Patterns(input.DateFormats ?? string.Empty);
        var times = PayloadFormatLayout.Patterns(input.TimeFormats ?? string.Empty);
        var separate = format.TimestampColumn.Length == 0 && dates.Count > 0 && times.Count > 0;
        format.DateFormats = separate ? string.Join(";", dates) : string.Empty;
        format.TimeFormats = separate ? string.Join(";", times) : string.Empty;
        format.TimestampFormats = separate ? string.Empty : string.Join(";", PayloadFormatLayout.Patterns(input.TimestampFormats ?? string.Empty));
    }

    private static IEnumerable<AttendancePayloadEventCode> CleanCodes(PayloadFormatInput input) =>
        input.EventCodes
            .Where(c => !string.IsNullOrWhiteSpace(c.Code))
            .Select(c => new AttendancePayloadEventCode { Code = Clean(c.Code), EventType = Enum.Parse<NormalizedEventType>(c.EventType, ignoreCase: true) });

    private static Dictionary<string, string> Validate(PayloadFormatInput input)
    {
        var errors = new Dictionary<string, string>();

        var name = Clean(input.Name);
        if (name.Length == 0 || name.Length > 255)
        {
            errors[nameof(PayloadFormatInput.Name)] = "A name is required (at most 255 characters).";
        }

        if (Clean(input.Description).Length > 1000)
        {
            errors[nameof(PayloadFormatInput.Description)] = "The description can be at most 1000 characters.";
        }

        if (!Enum.TryParse<AttendancePayloadKind>(input.PayloadKind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
        {
            errors[nameof(PayloadFormatInput.PayloadKind)] = $"The file kind must be one of: {string.Join(", ", Enum.GetNames<AttendancePayloadKind>())}.";
        }

        // A JSON or XML record is read into named fields, so its columns are always found by name.
        var structured = kind is AttendancePayloadKind.Json or AttendancePayloadKind.Xml;
        var hasHeader = input.HasHeaderRow || structured;

        if (!AllowedDelimiters.Contains(input.Delimiter))
        {
            errors[nameof(PayloadFormatInput.Delimiter)] = "The delimiter must be a comma, a semicolon, a vertical bar or a tab.";
        }

        var recordPath = Clean(input.RecordPath);
        if (recordPath.Length > 255)
        {
            errors[nameof(PayloadFormatInput.RecordPath)] = "The record path can be at most 255 characters.";
        }
        else if (recordPath.Length > 0 && !structured)
        {
            errors[nameof(PayloadFormatInput.RecordPath)] = "A record path belongs to a JSON or XML format.";
        }
        else if (recordPath.Contains("..", StringComparison.Ordinal) || recordPath.Contains("//", StringComparison.Ordinal))
        {
            errors[nameof(PayloadFormatInput.RecordPath)] = "The record path is names separated by '.' for JSON or '/' for XML, with no empty step.";
        }

        if (input.SheetNumber is < 1 or > 50)
        {
            errors[nameof(PayloadFormatInput.SheetNumber)] = "The sheet number must be between 1 and 50.";
        }

        var columns = new (string Field, string Label, string Value, bool Required)[]
        {
            (nameof(PayloadFormatInput.EmployeeColumn), "employee id column", Clean(input.EmployeeColumn), true),
            (nameof(PayloadFormatInput.TimestampColumn), "timestamp column", Clean(input.TimestampColumn), false),
            (nameof(PayloadFormatInput.DateColumn), "date column", Clean(input.DateColumn), false),
            (nameof(PayloadFormatInput.TimeColumn), "time column", Clean(input.TimeColumn), false),
            (nameof(PayloadFormatInput.TypeColumn), "type column", Clean(input.TypeColumn), false),
            (nameof(PayloadFormatInput.DirectionColumn), "direction column", Clean(input.DirectionColumn), false),
            (nameof(PayloadFormatInput.DeviceColumn), "device column", Clean(input.DeviceColumn), false),
            (nameof(PayloadFormatInput.TransactionIdColumn), "transaction id column", Clean(input.TransactionIdColumn), false),
        };
        foreach (var (field, label, value, required) in columns)
        {
            if (value.Length == 0)
            {
                if (required)
                {
                    errors[field] = $"The {label} is required.";
                }
            }
            else if (value.Length > 255)
            {
                errors[field] = $"The {label} can be at most 255 characters.";
            }
            else if (!hasHeader && !(int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1))
            {
                errors[field] = $"The file has no header row, so the {label} is the column's number: 1 for the first column.";
            }
            else if (BiometricWords.Any(word => value.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                errors[field] = $"The {label} looks like a biometric column. Biometric data is never read.";
            }
        }

        var kept = PayloadFormatLayout.Aliases(Clean(input.PayloadColumns));
        if (kept.Count > 0)
        {
            if (!hasHeader)
            {
                errors[nameof(PayloadFormatInput.PayloadColumns)] = "Columns can be chosen by name only when the file has a header row.";
            }
            else if (string.Join("|", kept).Length > 1000)
            {
                errors[nameof(PayloadFormatInput.PayloadColumns)] = "The columns to keep can be at most 1000 characters in all.";
            }
            else if (kept.Any(name => BiometricWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))))
            {
                errors[nameof(PayloadFormatInput.PayloadColumns)] = "A column to keep looks like a biometric column. Biometric data is never read.";
            }
        }

        var hasTimestamp = Clean(input.TimestampColumn).Length > 0;
        var hasDate = Clean(input.DateColumn).Length > 0;
        var hasTime = Clean(input.TimeColumn).Length > 0;
        if (hasTimestamp && (hasDate || hasTime))
        {
            errors[nameof(PayloadFormatInput.TimestampColumn)] = "Give either one timestamp column, or a date column and a time column, not both.";
        }
        else if (!hasTimestamp && !(hasDate && hasTime))
        {
            errors[nameof(PayloadFormatInput.TimestampColumn)] = "Give the timestamp column, or both a date column and a time column.";
        }

        var cultureTag = Clean(input.TimestampCulture);
        if (cultureTag.Length > 16 || !TimestampCultures.IsValid(cultureTag))
        {
            errors[nameof(PayloadFormatInput.TimestampCulture)] = "The language is not one this server knows. Choose one from the list, or leave it empty for English month names.";
        }

        var culture = TimestampCultures.Resolve(cultureTag);
        var dates = PayloadFormatLayout.Patterns(input.DateFormats ?? string.Empty);
        var times = PayloadFormatLayout.Patterns(input.TimeFormats ?? string.Empty);
        if (!hasTimestamp && (dates.Count > 0 || times.Count > 0))
        {
            ValidatePatternList(errors, nameof(PayloadFormatInput.DateFormats), "date", dates, p => IsUsableDatePattern(p, culture), "it needs a year, month and day, and no time, such as dd/MM/yyyy");
            ValidatePatternList(errors, nameof(PayloadFormatInput.TimeFormats), "time", times, p => IsUsableTimePattern(p, culture), "it needs an hour and minutes, and no date, such as HH:mm");
            if (!errors.ContainsKey(nameof(PayloadFormatInput.DateFormats)) && !errors.ContainsKey(nameof(PayloadFormatInput.TimeFormats)) && dates.Count * times.Count > PayloadFormatLayout.MaxPatternCombinations)
            {
                errors[nameof(PayloadFormatInput.TimeFormats)] = $"{dates.Count} date patterns with {times.Count} time patterns is {dates.Count * times.Count} combinations; at most {PayloadFormatLayout.MaxPatternCombinations} are allowed.";
            }
        }
        else
        {
            var patterns = PayloadFormatLayout.Patterns(input.TimestampFormats ?? string.Empty);
            ValidatePatternList(errors, nameof(PayloadFormatInput.TimestampFormats), "timestamp", patterns, p => IsUsablePattern(p, culture), "it needs a year, month, day and hour, such as dd/MM/yyyy HH:mm");
        }

        var codes = input.EventCodes.Where(c => !string.IsNullOrWhiteSpace(c.Code)).ToList();
        if (codes.Count > MaxEventCodes)
        {
            errors[nameof(PayloadFormatInput.EventCodes)] = $"At most {MaxEventCodes} labels can be mapped.";
        }
        else if (codes.Any(c => Clean(c.Code).Length > 100 || PayloadFormatLayout.CodeKey(c.Code).Length == 0))
        {
            errors[nameof(PayloadFormatInput.EventCodes)] = "Each label needs letters or digits, and at most 100 characters.";
        }
        else if (codes.Any(c => !Enum.TryParse<NormalizedEventType>(c.EventType, ignoreCase: true, out var type) || !Enum.IsDefined(type)))
        {
            errors[nameof(PayloadFormatInput.EventCodes)] = $"A label's event type must be one of: {string.Join(", ", Enum.GetNames<NormalizedEventType>())}.";
        }
        else if (codes.GroupBy(c => PayloadFormatLayout.CodeKey(c.Code)).Any(g => g.Count() > 1))
        {
            errors[nameof(PayloadFormatInput.EventCodes)] = "Two labels are the same once case, spaces and punctuation are ignored.";
        }

        return errors;
    }

    private static void ValidatePatternList(Dictionary<string, string> errors, string field, string kind, IReadOnlyList<string> patterns, Func<string, bool> isUsable, string needs)
    {
        if (patterns.Count == 0)
        {
            errors[field] = $"Give at least one {kind} pattern, {needs}.";
        }
        else if (patterns.Count > MaxPatternsPerList)
        {
            errors[field] = $"At most {MaxPatternsPerList} {kind} patterns can be listed.";
        }
        else if (string.Join(";", patterns).Length > 1000)
        {
            errors[field] = "The patterns can be at most 1000 characters in all.";
        }
        else if (patterns.FirstOrDefault(p => !isUsable(p)) is { } bad)
        {
            errors[field] = $"'{bad}' is not a usable pattern for the {kind}: {needs}, and it must read back the text it writes.";
        }
    }

    /// <summary>The pattern with its quoted literal text removed, so letters inside quotes are not taken for format specifiers.</summary>
    private static string WithoutLiterals(string pattern) => Regex.Replace(pattern, "'[^']*'", string.Empty);

    /// <summary>A pattern is usable when it names the year, month, day and hour, and formats a time that it can read back.</summary>
    private static bool IsUsablePattern(string pattern, CultureInfo culture)
    {
        // A single character is a standard format ("d", "G"), not a pattern.
        var specifiers = WithoutLiterals(pattern);
        return pattern.Length >= 6
            && specifiers.Contains('y') && specifiers.Contains('M') && specifiers.Contains('d') && (specifiers.Contains('H') || specifiers.Contains('h'))
            && ReadsItselfBack(pattern, culture);
    }

    /// <summary>A date pattern names the year, month and day and nothing of the time of day.</summary>
    private static bool IsUsableDatePattern(string pattern, CultureInfo culture)
    {
        var specifiers = WithoutLiterals(pattern);
        return pattern.Length >= 5
            && specifiers.Contains('y') && specifiers.Contains('M') && specifiers.Contains('d')
            && !specifiers.Any(c => c is 'H' or 'h' or 'm' or 's' or 't' or 'f' or 'F' or 'z' or 'K')
            && ReadsItselfBack(pattern, culture);
    }

    /// <summary>A time pattern names the hour and the minutes and nothing of the date.</summary>
    private static bool IsUsableTimePattern(string pattern, CultureInfo culture)
    {
        var specifiers = WithoutLiterals(pattern);
        return pattern.Length >= 4
            && (specifiers.Contains('H') || specifiers.Contains('h')) && specifiers.Contains('m')
            && !specifiers.Any(c => c is 'y' or 'M' or 'd' or 'z' or 'K' or 'g')
            && ReadsItselfBack(pattern, culture);
    }

    private static bool ReadsItselfBack(string pattern, CultureInfo culture)
    {
        try
        {
            var sample = new DateTime(2026, 3, 14, 15, 9, 26);
            return DateTime.TryParseExact(sample.ToString(pattern, culture), pattern, culture, DateTimeStyles.None, out _);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the changed format as the next version, leaving the old row exactly as the punches were read by it, and moves what
    /// pointed at the old one (integrations, connectors) to the new one.
    /// </summary>
    private async Task<SaveResult> CreateVersionAsync(AttendancePayloadFormat old, PayloadFormatInput input, string before, CancellationToken cancellationToken)
    {
        var next = new AttendancePayloadFormat { Version = old.Version + 1, SupersedesId = old.Id, ConnectorId = old.ConnectorId };
        Apply(input, next);
        foreach (var code in CleanCodes(input))
        {
            next.EventCodes.Add(code);
        }

        _db.AttendancePayloadFormats.Add(next);
        foreach (var integration in await _db.AttendanceIntegrations.Where(i => i.PayloadFormatId == old.Id).ToListAsync(cancellationToken))
        {
            integration.PayloadFormat = next;
        }

        foreach (var connector in await _db.AttendanceConnectorDefinitions.Where(c => c.DefaultPayloadFormatId == old.Id).ToListAsync(cancellationToken))
        {
            connector.DefaultPayloadFormat = next;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendancePayloadFormat), next.Id, $"File format '{next.Name}' version {next.Version} replaces version {old.Version} (id {old.Id}), which punches were read by. Before: {before}. After: {Summary(next)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(next.Id);
    }

    private async Task CheckNameAsync(PayloadFormatInput input, int? existingId, Dictionary<string, string> errors, CancellationToken cancellationToken)
    {
        var name = Clean(input.Name);

        // The format's own earlier versions carry its name too: they are the same format, not another one.
        var lineage = new List<int>();
        for (var at = existingId; at is not null;)
        {
            lineage.Add(at.Value);
            at = await _db.AttendancePayloadFormats.AsNoTracking().Where(f => f.Id == at).Select(f => f.SupersedesId).SingleOrDefaultAsync(cancellationToken);
        }

        if (name.Length > 0 && !errors.ContainsKey(nameof(PayloadFormatInput.Name))
            && await _db.AttendancePayloadFormats.AnyAsync(f => f.Name.ToLower() == name.ToLower() && !lineage.Contains(f.Id), cancellationToken))
        {
            errors[nameof(PayloadFormatInput.Name)] = "Another file format already has this name.";
        }
    }
}
