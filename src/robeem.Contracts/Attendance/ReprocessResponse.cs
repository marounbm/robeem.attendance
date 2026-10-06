namespace robeem.Contracts.Attendance;

/// <summary>What reprocessing a day did.</summary>
public sealed class ReprocessResponse
{
    /// <summary>Gets the records that changed and so got a new version.</summary>
    public int RecordsWritten { get; init; }

    /// <summary>Gets the records whose calculation came out the same and were kept.</summary>
    public int RecordsUnchanged { get; init; }

    public int RecordsRetired { get; init; }

    public int ExceptionsRaised { get; init; }

    public int ExceptionsResolved { get; init; }
}
