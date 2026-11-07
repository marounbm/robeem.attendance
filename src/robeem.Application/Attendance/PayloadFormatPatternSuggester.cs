using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Finds which of the common patterns (<see cref="TimestampPatternCatalog"/>) read the timestamps of a sample file, for the format
/// preview to suggest. It reads through the format's own interpreter, so a suggestion reads the file exactly as the saved format would
/// (culture, offsets and all). Nothing is stored.
/// </summary>
internal sealed class PayloadFormatPatternSuggester
{
    private const int MaxTimestamps = 200;
    private const int MaxSuggestions = 8;

    private readonly ICustomFormatInterpreterFactory _interpreters;

    public PayloadFormatPatternSuggester(ICustomFormatInterpreterFactory interpreters)
    {
        _interpreters = interpreters;
    }

    /// <summary>The suggestions for the timestamps of <paramref name="transactions"/>; null when none has a timestamp text.</summary>
    public PayloadFormatPatternSuggestions? Suggest(AttendancePayloadFormat format, IReadOnlyList<ExternalAttendanceTransaction> transactions)
    {
        var texts = transactions
            .Select(t => t.TransactionTimestampRaw.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxTimestamps)
            .ToList();
        if (texts.Count == 0)
        {
            return null;
        }

        var separate = PayloadFormatLayout.UsesSeparatePatterns(format);
        return separate ? SuggestSeparate(format, texts) : SuggestJoined(format, texts);
    }

    // A format that reads only by the given patterns, in the language of the real one.
    private static AttendancePayloadFormat Candidate(AttendancePayloadFormat format, string timestamp, string date, string time) => new()
    {
        TimestampCulture = format.TimestampCulture,
        TimestampFormats = timestamp,
        DateFormats = date,
        TimeFormats = time,
    };

    private static IReadOnlyList<PayloadFormatPatternSuggestion> Best(List<PayloadFormatPatternSuggestion> results, int total, out bool partial)
    {
        if (results.Count == 0)
        {
            partial = false;
            return [];
        }

        // Whatever reads the whole sample; failing that, the patterns that read the most of it.
        var top = results.Max(r => r.Reads);
        partial = top < total;
        return results.Where(r => r.Reads == top).Take(MaxSuggestions).ToList();
    }

    private PayloadFormatPatternSuggestions SuggestJoined(AttendancePayloadFormat format, IReadOnlyList<string> texts)
    {
        var inFormat = PayloadFormatLayout.Patterns(format.TimestampFormats).ToHashSet(StringComparer.Ordinal);
        var results = TimestampPatternCatalog.Combined
            .Select(option => Evaluate(Candidate(format, timestamp: option.Pattern, date: string.Empty, time: string.Empty), option.Pattern, texts, inFormat))
            .Where(r => r.Reads > 0)
            .ToList();
        var best = Best(results, texts.Count, out var partial);
        return new PayloadFormatPatternSuggestions { Separate = false, TimestampsChecked = texts.Count, Timestamp = best, Partial = partial };
    }

    private PayloadFormatPatternSuggestions SuggestSeparate(AttendancePayloadFormat format, IReadOnlyList<string> texts)
    {
        // The text is the date and the time joined by a space, so each pair of a date and a time pattern is tried as one.
        var pairs = new List<(PayloadFormatPatternSuggestion Date, PayloadFormatPatternSuggestion Time, int Reads)>();
        var datesInFormat = PayloadFormatLayout.Patterns(format.DateFormats).ToHashSet(StringComparer.Ordinal);
        var timesInFormat = PayloadFormatLayout.Patterns(format.TimeFormats).ToHashSet(StringComparer.Ordinal);
        foreach (var date in TimestampPatternCatalog.Dates)
        {
            foreach (var time in TimestampPatternCatalog.Times)
            {
                var read = Evaluate(Candidate(format, timestamp: string.Empty, date: date.Pattern, time: time.Pattern), date.Pattern + " " + time.Pattern, texts, new HashSet<string>());
                if (read.Reads > 0)
                {
                    pairs.Add((
                        new PayloadFormatPatternSuggestion { Pattern = date.Pattern, Reads = read.Reads, Example = read.Example, InFormat = datesInFormat.Contains(date.Pattern) },
                        new PayloadFormatPatternSuggestion { Pattern = time.Pattern, Reads = read.Reads, Example = read.Example, InFormat = timesInFormat.Contains(time.Pattern) },
                        read.Reads));
                }
            }
        }

        // The pairs that read the most decide: every one of them when they read the whole sample, else the best of what there is.
        var top = pairs.Count == 0 ? 0 : pairs.Max(p => p.Reads);
        var bestPairs = pairs.Where(p => p.Reads == top).ToList();
        return new PayloadFormatPatternSuggestions
        {
            Separate = true,
            TimestampsChecked = texts.Count,
            Date = bestPairs.Select(p => p.Date).DistinctBy(s => s.Pattern).Take(MaxSuggestions).ToList(),
            Time = bestPairs.Select(p => p.Time).DistinctBy(s => s.Pattern).Take(MaxSuggestions).ToList(),
            Partial = top > 0 && top < texts.Count,
        };
    }

    private PayloadFormatPatternSuggestion Evaluate(AttendancePayloadFormat candidate, string label, IReadOnlyList<string> texts, HashSet<string> inFormat)
    {
        var interpreter = _interpreters.Create(candidate);
        var reads = 0;
        var example = string.Empty;
        foreach (var text in texts)
        {
            if (interpreter.ParseTimestamp(text) is not { } timestamp)
            {
                continue;
            }

            reads++;
            if (example.Length == 0)
            {
                example = $"{text} → {timestamp.WallClock:yyyy-MM-dd HH:mm:ss}";
            }
        }

        return new PayloadFormatPatternSuggestion { Pattern = label, Reads = reads, Example = example, InFormat = inFormat.Contains(label) };
    }
}
