namespace robeem.Contracts.Attendance;

/// <summary>What one label of a file's type or direction column means.</summary>
public sealed class PayloadFormatEventCodeInput
{
    /// <summary>Gets or sets the label as written in the file.</summary>
    public string? Code { get; set; }

    /// <summary>Gets or sets the event type name: CheckIn, CheckOut, BreakOut or BreakIn.</summary>
    public string EventType { get; set; } = "CheckIn";
}
