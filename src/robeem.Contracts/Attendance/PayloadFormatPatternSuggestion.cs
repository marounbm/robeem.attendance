namespace robeem.Contracts.Attendance;

/// <summary>One pattern that reads the sample, and what it made of the first timestamp it read.</summary>
public sealed class PayloadFormatPatternSuggestion
{
    public string Pattern { get; init; } = string.Empty;

    /// <summary>Gets how many of the checked timestamps the pattern reads.</summary>
    public int Reads { get; init; }

    /// <summary>Gets the first timestamp text it read, and how it read it, such as <c>03/04/2026 08:30 → 2026-04-03 08:30:00</c>.</summary>
    public string Example { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the format has this pattern already.</summary>
    public bool InFormat { get; init; }
}
