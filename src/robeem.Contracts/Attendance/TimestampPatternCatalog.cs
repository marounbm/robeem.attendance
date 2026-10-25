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
    ];

    private static readonly string[] TimePatterns =
    [
        "HH:mm", "HH:mm:ss", "H:mm", "H:mm:ss", "hh:mm tt", "hh:mm:ss tt", "h:mm tt", "HHmm", "HHmmss",
    ];

    private static readonly string[] CombinedPatterns =
    [
        "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "MM/dd/yyyy HH:mm:ss", "MM/dd/yyyy hh:mm:ss tt", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss",
        "dd-MMM-yy HH:mm:ss", "d/M/yyyy H:mm", "dd.MM.yyyy HH:mm:ss", "yyyyMMddHHmmss",
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

    private static List<TimestampPatternOption> Options(string[] patterns) =>
        patterns.Select(p => new TimestampPatternOption(p, Sample.ToString(p, CultureInfo.InvariantCulture))).ToList();
}
