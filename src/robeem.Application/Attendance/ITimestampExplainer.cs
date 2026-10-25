namespace robeem.Application.Attendance;

/// <summary>
/// Implemented by an interpreter that can say which of its patterns read a timestamp text. Used by the format preview to show
/// that two patterns read the same text differently (so the order of the patterns decides), never to interpret a punch.
/// </summary>
public interface ITimestampExplainer
{
    /// <summary>Gets every pattern that reads <paramref name="timestampText"/>, in the order the patterns are tried.</summary>
    IReadOnlyList<TimestampMatch> Explain(string timestampText);
}
