using System.Globalization;

namespace robeem.Contracts.Attendance;

/// <summary>
/// The patterns the format form offers as a starting point. A format stores the pattern text itself, never a catalog
/// entry, so changing this list never changes what a saved format reads.
/// </summary>
public static class TimestampPatternCatalog
{
    private static readonly DateTime Sample = new(2026, 3, 14, 15, 9, 26);

    private static readonly string[] DatePatterns =
    [
        "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy", "yyyy/MM/dd", "d/M/yyyy", "dd-MMM-yy", "dd-MMM-yyyy", "dd MMM yyyy", "yyyyMMdd",
        "dd/MM/yy", "MM/dd/yy", "M/d/yyyy", "d-M-yyyy", "dd-MM-yy", "yy/MM/dd", "yyyy.MM.dd", "d MMM yyyy", "dd MMMM yyyy", "d MMMM yyyy", "MMM d, yyyy", "MMMM d, yyyy",
        "ddd dd/MM/yyyy", "ddd, dd MMM yyyy",
    ];

    private static readonly string[] TimePatterns =
    [
        "HH:mm", "HH:mm:ss", "H:mm", "H:mm:ss", "hh:mm tt", "hh:mm:ss tt", "h:mm tt", "HHmm", "HHmmss",
        "h:mm:ss tt", "hh:mmtt", "hh:mm:sstt", "HH:mm:ss.fff", "HH:mm:ss.ffffff", "H:mm:ss.fff", "HH.mm", "HH.mm.ss", "H.mm", "HH-mm-ss",
    ];

    private static readonly string[] CombinedPatterns =
    [
        "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "MM/dd/yyyy HH:mm:ss", "MM/dd/yyyy hh:mm:ss tt", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss",
        "dd-MMM-yy HH:mm:ss", "d/M/yyyy H:mm", "dd.MM.yyyy HH:mm:ss", "yyyyMMddHHmmss",
        "dd/MM/yyyy hh:mm tt", "dd/MM/yyyy hh:mm:ss tt", "d/M/yyyy H:mm:ss", "d/M/yyyy h:mm:ss tt", "dd/MM/yy HH:mm", "dd/MM/yy HH:mm:ss",
        "MM/dd/yyyy HH:mm", "MM/dd/yyyy hh:mm tt", "M/d/yyyy H:mm", "M/d/yyyy H:mm:ss", "M/d/yyyy h:mm tt", "M/d/yyyy h:mm:ss tt", "MM/dd/yy HH:mm:ss",
        "dd-MM-yyyy HH:mm", "dd-MM-yyyy HH:mm:ss", "dd-MM-yy HH:mm", "dd-MM-yy HH:mm:ss", "dd.MM.yyyy HH:mm", "dd.MM.yy HH:mm:ss",
        "yyyy/MM/dd HH:mm", "yyyy/MM/dd HH:mm:ss", "yyyy.MM.dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-dd hh:mm:ss tt", "yyyyMMdd HHmmss", "yyyyMMddHHmm", "yyyyMMddHHmmssfff",
        "dd-MMM-yy HH:mm", "dd-MMM-yyyy HH:mm", "dd-MMM-yyyy HH:mm:ss", "dd-MMM-yy hh:mm:ss tt", "dd MMM yyyy HH:mm", "dd MMM yyyy HH:mm:ss", "d MMM yyyy h:mm tt",
        "dd MMMM yyyy HH:mm", "MMM d, yyyy h:mm:ss tt", "MMMM d, yyyy h:mm tt", "ddd dd/MM/yyyy HH:mm:ss", "ddd, dd MMM yyyy HH:mm:ss",
    ];

    /// <summary>Gets the cultures offered for month and day names: the language tag and what to call it. The empty tag is invariant (English names).</summary>
    public static IReadOnlyList<(string Tag, string Name)> Cultures { get; } =
    [
        (string.Empty, "Invariant (English names)"),
        ("en-US", "English (United States)"),
        ("en-GB", "English (United Kingdom)"),
        ("fr-FR", "French (France)"),
        ("ar-LB", "Arabic (Lebanon)"),
        ("ar-AE", "Arabic (United Arab Emirates)"),
        ("ar-EG", "Arabic (Egypt)"),
        ("de-DE", "German (Germany)"),
        ("es-ES", "Spanish (Spain)"),
        ("tr-TR", "Turkish (Turkey)"),
    ];

    /// <summary>Gets the patterns of a column that holds only a date.</summary>
    public static IReadOnlyList<TimestampPatternOption> Dates { get; } = Options(DatePatterns);

    /// <summary>Gets the patterns of a column that holds only a time of day.</summary>
    public static IReadOnlyList<TimestampPatternOption> Times { get; } = Options(TimePatterns);

    /// <summary>Gets the patterns of a column that holds the date and the time together.</summary>
    public static IReadOnlyList<TimestampPatternOption> Combined { get; } = Options(CombinedPatterns);

    // Sorted by the pattern text (letters ignoring case, then as written), so a pattern is found in the drop-down rather than hunted for.
    private static List<TimestampPatternOption> Options(string[] patterns) =>
        patterns
            .Order(StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p, StringComparer.Ordinal)
            .Select(p => new TimestampPatternOption(p, Sample.ToString(p, CultureInfo.InvariantCulture)))
            .ToList();
}
