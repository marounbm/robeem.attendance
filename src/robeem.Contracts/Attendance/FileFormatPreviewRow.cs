namespace robeem.Contracts.Attendance;

/// <summary>One row of a sample file, read by an unsaved format.</summary>
public sealed class FileFormatPreviewRow
{
    public int RowNumber { get; init; }

    public string? ExternalEmployeeId { get; init; }

    public string TimestampText { get; init; } = string.Empty;

    /// <summary>Gets the timestamp as it was understood, as wall-clock text; null when it did not parse.</summary>
    public string? ParsedTimestamp { get; init; }

    public string? TypeText { get; init; }

    /// <summary>Gets the event type the label maps to; null for an unlabeled punch or a label that is not mapped.</summary>
    public string? EventType { get; init; }

    public string? DeviceText { get; init; }

    /// <summary>Gets what is wrong with the row (it would be stored as failed); empty when nothing is.</summary>
    public string Problem { get; init; } = string.Empty;
}
