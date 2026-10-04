namespace robeem.Application.Attendance;

/// <summary>A half-open range of instants, <c>[From, To)</c>.</summary>
public sealed record DateRange(DateTimeOffset From, DateTimeOffset To);
