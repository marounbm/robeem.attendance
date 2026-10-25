namespace robeem.Application.Attendance;

/// <summary>One pattern that reads a timestamp text, and what it reads it as (wall-clock text, with the offset when the text has one).</summary>
/// <param name="Pattern">The pattern that matched.</param>
/// <param name="Reading">The instant it gives, as <c>yyyy-MM-dd HH:mm:ss</c> plus any offset.</param>
public sealed record TimestampMatch(string Pattern, string Reading);
