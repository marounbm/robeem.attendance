namespace robeem.Contracts.Attendance;

/// <summary>
/// The common patterns that read the timestamps of a sample file, found by trying each of <see cref="TimestampPatternCatalog"/> on them.
/// A suggestion is advice for the form, never saved by itself.
/// </summary>
public sealed class PayloadFormatPatternSuggestions
{
    /// <summary>Gets a value indicating whether the format reads a date column and a time column by patterns of their own.</summary>
    public bool Separate { get; init; }

    /// <summary>Gets how many different timestamp texts the sample was checked on (at most 200).</summary>
    public int TimestampsChecked { get; init; }

    /// <summary>Gets the patterns for the timestamp column; empty when <see cref="Separate"/>.</summary>
    public IReadOnlyList<PayloadFormatPatternSuggestion> Timestamp { get; init; } = [];

    /// <summary>Gets the patterns for the date column; empty unless <see cref="Separate"/>.</summary>
    public IReadOnlyList<PayloadFormatPatternSuggestion> Date { get; init; } = [];

    /// <summary>Gets the patterns for the time column; empty unless <see cref="Separate"/>.</summary>
    public IReadOnlyList<PayloadFormatPatternSuggestion> Time { get; init; } = [];

    /// <summary>Gets a value indicating whether nothing in the catalog reads every timestamp, so the suggestions are the best partial ones.</summary>
    public bool Partial { get; init; }

    /// <summary>Gets a value indicating whether the catalog reads none of the timestamps; the format needs a pattern of its own.</summary>
    public bool NoneRead => Timestamp.Count == 0 && Date.Count == 0 && Time.Count == 0;
}
