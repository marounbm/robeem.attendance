namespace robeem.Application.Attendance;

/// <summary>Outcome of <see cref="IAttendanceAdapter.ValidateConnectionAsync"/>.</summary>
public sealed record ConnectionValidationResult(bool IsValid, string? Message = null);
