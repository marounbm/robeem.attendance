namespace robeem.Contracts.Attendance;

/// <summary>A common .NET date-time pattern with an example of what it reads, for the format form to offer.</summary>
/// <param name="Pattern">The pattern itself, as stored.</param>
/// <param name="Example">14 March 2026, 15:09:26 written in that pattern.</param>
public sealed record TimestampPatternOption(string Pattern, string Example);
