namespace robeem.Application.Attendance;

/// <summary>Outcome of <see cref="IAttendanceProvider.ValidateConnectionAsync"/>.</summary>
public sealed record ConnectionValidationResult(bool IsValid, string? Message = null);
