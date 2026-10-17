using System.Globalization;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceFileFormatService : IAttendanceFileFormatService
{
    private const int PreviewRows = 20;
    private const int MaxEventCodes = 50;

    private static readonly string[] AllowedDelimiters = [",", ";", "|", "\t"];

    // A column that can only hold a biometric template is never read, whatever the file calls it (CLAUDE.md §1.10).
    private static readonly string[] BiometricWords = ["template", "biometric", "fingerprint", "faceprint"];

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;
    private readonly ICustomFormatFileReader _reader;
    private readonly ICustomFormatInterpreterFactory _interpreters;

    public AttendanceFileFormatService(IRobeemDbContext db, IAttendanceAuditTrail audit, ICustomFormatFileReader reader, ICustomFormatInterpreterFactory interpreters)
    {
        _db = db;
        _audit = audit;
        _reader = reader;
        _interpreters = interpreters;
    }

    public async Task<IReadOnlyList<FileFormatListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.AttendanceFileFormats.AsNoTracking()
            .OrderBy(f => f.Name)
            .Select(f => new
            {
                f.Id,
                f.Name,
                f.Description,
                f.FileKind,
                f.Delimiter,
                f.IsActive,
                EventCodeCount = f.EventCodes.Count,
                IntegrationCount = _db.AttendanceIntegrations.Count(i => i.FileFormatId == f.Id),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(f => new FileFormatListItem
        {
            Id = f.Id,
            Name = f.Name,
            Description = f.Description,
            FileKind = f.FileKind.ToString(),
            Delimiter = FileFormatLayout.DelimiterName(f.Delimiter),
            IsActive = f.IsActive,
            EventCodeCount = f.EventCodeCount,
            IntegrationCount = f.IntegrationCount,
        }).ToList();
    }

    public async Task<IReadOnlyList<FileFormatOption>> ListOptionsAsync(CancellationToken cancellationToken) =>
        await _db.AttendanceFileFormats.AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.Name)
            .Select(f => new FileFormatOption { Id = f.Id, Name = f.Name })
            .ToListAsync(cancellationToken);

    public async Task<FileFormatInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        var format = await _db.AttendanceFileFormats.AsNoTracking().Include(f => f.EventCodes).SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        return format is null
            ? null
            : new FileFormatInput
            {
                Name = format.Name,
                Description = format.Description,
                FileKind = format.FileKind.ToString(),
                Delimiter = format.Delimiter,
                HasHeaderRow = format.HasHeaderRow,
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
                TimestampFormats = string.Join(Environment.NewLine, FileFormatLayout.Patterns(format.TimestampFormats)),
                EventCodes = format.EventCodes.OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase).Select(c => new FileFormatEventCodeInput { Code = c.Code, EventType = c.EventType.ToString() }).ToList(),
            };
    }

    public async Task<SaveResult> CreateAsync(FileFormatInput input, CancellationToken cancellationToken)
    {
        var errors = Validate(input);
        await CheckNameAsync(input, null, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var format = new AttendanceFileFormat();
        Apply(input, format);
        foreach (var code in CleanCodes(input))
        {
            format.EventCodes.Add(code);
        }

        _db.AttendanceFileFormats.Add(format);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceFileFormat), format.Id, $"File format '{format.Name}' created: {Summary(format)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(format.Id);
    }

    public async Task<SaveResult> UpdateAsync(int id, FileFormatInput input, CancellationToken cancellationToken)
    {
        var format = await _db.AttendanceFileFormats.Include(f => f.EventCodes).SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (format is null)
        {
            return SaveResult.Missing();
        }

        var errors = Validate(input);
        await CheckNameAsync(input, id, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = Summary(format);
        Apply(input, format);

        // Codes are matched by their normalized label: a kept one is updated in place, a new one added, a dropped one removed.
        var wanted = CleanCodes(input).ToDictionary(c => FileFormatLayout.CodeKey(c.Code));
        foreach (var existing in format.EventCodes.ToList())
        {
            if (wanted.Remove(FileFormatLayout.CodeKey(existing.Code), out var replacement))
            {
                existing.Code = replacement.Code;
                existing.EventType = replacement.EventType;
            }
            else
            {
                _db.AttendanceFileFormatEventCodes.Remove(existing);
            }
        }

        foreach (var added in wanted.Values)
        {
            format.EventCodes.Add(added);
        }

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceFileFormat), format.Id, $"File format '{format.Name}' changed. Before: {before}. After: {Summary(format)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(format.Id);
    }

    public async Task<FileFormatPreview> PreviewAsync(FileFormatInput input, string filePath, CancellationToken cancellationToken)
    {
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return new FileFormatPreview { Problem = "The format is not valid yet: " + string.Join(" ", errors.Values) };
        }

        // The format is built in memory and never added to the context: nothing is stored.
        var format = new AttendanceFileFormat();
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
            return new FileFormatPreview { Problem = ex.Message };
        }

        var interpreter = _interpreters.Create(format);
        var rows = transactions.Take(PreviewRows).Select((t, index) => ToPreviewRow(index + 1, t, interpreter)).ToList();
        return new FileFormatPreview { TotalRows = transactions.Count, Rows = rows };
    }

    private static FileFormatPreviewRow ToPreviewRow(int number, ExternalAttendanceTransaction transaction, IAttendanceEventInterpreter interpreter)
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

        return new FileFormatPreviewRow
        {
            RowNumber = number,
            ExternalEmployeeId = transaction.ExternalEmployeeId,
            TimestampText = transaction.TransactionTimestampRaw,
            ParsedTimestamp = timestamp?.WallClock.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + (timestamp?.Offset is { } offset ? " " + (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm", CultureInfo.InvariantCulture) : string.Empty),
            TypeText = label,
            EventType = eventType?.ToString(),
            DeviceText = transaction.DeviceExternalId,
            Problem = string.Join("; ", problems),
        };
    }

    private static string Summary(AttendanceFileFormat format) =>
        $"{format.FileKind}, delimiter {FileFormatLayout.DelimiterName(format.Delimiter)}, header {(format.HasHeaderRow ? "yes" : "no")}, employee '{format.EmployeeColumn}', timestamp '{(format.TimestampColumn.Length > 0 ? format.TimestampColumn : format.DateColumn + "+" + format.TimeColumn)}', patterns '{format.TimestampFormats}', {format.EventCodes.Count} codes, {(format.IsActive ? "active" : "inactive")}";

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    /// <summary>A column setting as it is stored: its headers, trimmed, separated by <c>|</c>, none empty.</summary>
    private static string Headers(string? value) => string.Join("|", FileFormatLayout.Aliases(Clean(value)));

    private static void Apply(FileFormatInput input, AttendanceFileFormat format)
    {
        format.Name = Clean(input.Name);
        format.Description = Clean(input.Description);
        format.FileKind = Enum.Parse<AttendanceFileKind>(input.FileKind, ignoreCase: true);
        format.Delimiter = input.Delimiter;
        format.HasHeaderRow = input.HasHeaderRow;
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
        format.TimestampFormats = string.Join(";", FileFormatLayout.Patterns(input.TimestampFormats ?? string.Empty));
    }

    private static IEnumerable<AttendanceFileFormatEventCode> CleanCodes(FileFormatInput input) =>
        input.EventCodes
            .Where(c => !string.IsNullOrWhiteSpace(c.Code))
            .Select(c => new AttendanceFileFormatEventCode { Code = Clean(c.Code), EventType = Enum.Parse<NormalizedEventType>(c.EventType, ignoreCase: true) });

    private static Dictionary<string, string> Validate(FileFormatInput input)
    {
        var errors = new Dictionary<string, string>();

        var name = Clean(input.Name);
        if (name.Length == 0 || name.Length > 255)
        {
            errors[nameof(FileFormatInput.Name)] = "A name is required (at most 255 characters).";
        }

        if (Clean(input.Description).Length > 1000)
        {
            errors[nameof(FileFormatInput.Description)] = "The description can be at most 1000 characters.";
        }

        if (!Enum.TryParse<AttendanceFileKind>(input.FileKind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
        {
            errors[nameof(FileFormatInput.FileKind)] = $"The file kind must be one of: {string.Join(", ", Enum.GetNames<AttendanceFileKind>())}.";
        }

        if (!AllowedDelimiters.Contains(input.Delimiter))
        {
            errors[nameof(FileFormatInput.Delimiter)] = "The delimiter must be a comma, a semicolon, a vertical bar or a tab.";
        }

        if (input.SheetNumber is < 1 or > 50)
        {
            errors[nameof(FileFormatInput.SheetNumber)] = "The sheet number must be between 1 and 50.";
        }

        var columns = new (string Field, string Label, string Value, bool Required)[]
        {
            (nameof(FileFormatInput.EmployeeColumn), "employee id column", Clean(input.EmployeeColumn), true),
            (nameof(FileFormatInput.TimestampColumn), "timestamp column", Clean(input.TimestampColumn), false),
            (nameof(FileFormatInput.DateColumn), "date column", Clean(input.DateColumn), false),
            (nameof(FileFormatInput.TimeColumn), "time column", Clean(input.TimeColumn), false),
            (nameof(FileFormatInput.TypeColumn), "type column", Clean(input.TypeColumn), false),
            (nameof(FileFormatInput.DirectionColumn), "direction column", Clean(input.DirectionColumn), false),
            (nameof(FileFormatInput.DeviceColumn), "device column", Clean(input.DeviceColumn), false),
            (nameof(FileFormatInput.TransactionIdColumn), "transaction id column", Clean(input.TransactionIdColumn), false),
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
            else if (!input.HasHeaderRow && !(int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1))
            {
                errors[field] = $"The file has no header row, so the {label} is the column's number: 1 for the first column.";
            }
            else if (BiometricWords.Any(word => value.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                errors[field] = $"The {label} looks like a biometric column. Biometric data is never read.";
            }
        }

        var kept = FileFormatLayout.Aliases(Clean(input.PayloadColumns));
        if (kept.Count > 0)
        {
            if (!input.HasHeaderRow)
            {
                errors[nameof(FileFormatInput.PayloadColumns)] = "Columns can be chosen by name only when the file has a header row.";
            }
            else if (string.Join("|", kept).Length > 1000)
            {
                errors[nameof(FileFormatInput.PayloadColumns)] = "The columns to keep can be at most 1000 characters in all.";
            }
            else if (kept.Any(name => BiometricWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))))
            {
                errors[nameof(FileFormatInput.PayloadColumns)] = "A column to keep looks like a biometric column. Biometric data is never read.";
            }
        }

        var hasTimestamp = Clean(input.TimestampColumn).Length > 0;
        var hasDate = Clean(input.DateColumn).Length > 0;
        var hasTime = Clean(input.TimeColumn).Length > 0;
        if (hasTimestamp && (hasDate || hasTime))
        {
            errors[nameof(FileFormatInput.TimestampColumn)] = "Give either one timestamp column, or a date column and a time column, not both.";
        }
        else if (!hasTimestamp && !(hasDate && hasTime))
        {
            errors[nameof(FileFormatInput.TimestampColumn)] = "Give the timestamp column, or both a date column and a time column.";
        }

        var patterns = FileFormatLayout.Patterns(input.TimestampFormats ?? string.Empty);
        if (patterns.Count == 0)
        {
            errors[nameof(FileFormatInput.TimestampFormats)] = "Give at least one timestamp pattern, such as dd/MM/yyyy HH:mm.";
        }
        else if (string.Join(";", patterns).Length > 1000)
        {
            errors[nameof(FileFormatInput.TimestampFormats)] = "The patterns can be at most 1000 characters in all.";
        }
        else if (patterns.FirstOrDefault(p => !IsUsablePattern(p)) is { } bad)
        {
            errors[nameof(FileFormatInput.TimestampFormats)] = $"'{bad}' is not a usable pattern: it needs a year, month, day and hour, such as dd/MM/yyyy HH:mm.";
        }

        var codes = input.EventCodes.Where(c => !string.IsNullOrWhiteSpace(c.Code)).ToList();
        if (codes.Count > MaxEventCodes)
        {
            errors[nameof(FileFormatInput.EventCodes)] = $"At most {MaxEventCodes} labels can be mapped.";
        }
        else if (codes.Any(c => Clean(c.Code).Length > 100 || FileFormatLayout.CodeKey(c.Code).Length == 0))
        {
            errors[nameof(FileFormatInput.EventCodes)] = "Each label needs letters or digits, and at most 100 characters.";
        }
        else if (codes.Any(c => !Enum.TryParse<NormalizedEventType>(c.EventType, ignoreCase: true, out var type) || !Enum.IsDefined(type)))
        {
            errors[nameof(FileFormatInput.EventCodes)] = $"A label's event type must be one of: {string.Join(", ", Enum.GetNames<NormalizedEventType>())}.";
        }
        else if (codes.GroupBy(c => FileFormatLayout.CodeKey(c.Code)).Any(g => g.Count() > 1))
        {
            errors[nameof(FileFormatInput.EventCodes)] = "Two labels are the same once case, spaces and punctuation are ignored.";
        }

        return errors;
    }

    /// <summary>A pattern is usable when it names the year, month, day and hour, and formats a time that it can read back.</summary>
    private static bool IsUsablePattern(string pattern)
    {
        // A single character is a standard format ("d", "G"), not a pattern.
        if (pattern.Length < 6 || !pattern.Contains('y') || !pattern.Contains('M') || !pattern.Contains('d') || !(pattern.Contains('H') || pattern.Contains('h')))
        {
            return false;
        }

        try
        {
            var sample = new DateTime(2026, 3, 14, 15, 9, 26);
            return DateTime.TryParseExact(sample.ToString(pattern, CultureInfo.InvariantCulture), pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task CheckNameAsync(FileFormatInput input, int? existingId, Dictionary<string, string> errors, CancellationToken cancellationToken)
    {
        var name = Clean(input.Name);
        if (name.Length > 0 && !errors.ContainsKey(nameof(FileFormatInput.Name))
            && await _db.AttendanceFileFormats.AnyAsync(f => f.Name.ToLower() == name.ToLower() && f.Id != existingId, cancellationToken))
        {
            errors[nameof(FileFormatInput.Name)] = "Another file format already has this name.";
        }
    }
}
