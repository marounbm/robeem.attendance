namespace robeem.Contracts.Attendance;

/// <summary>One vendor field and the raw-transaction field it feeds.</summary>
public sealed class FieldMappingRule
{
    /// <summary>Gets the vendor's field or column name.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Gets the raw transaction field it fills, or "payload only" when it is kept in the payload and nothing else.</summary>
    public string Target { get; init; } = string.Empty;

    public string? Note { get; init; }
}
