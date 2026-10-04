namespace robeem.Application.Attendance;

/// <summary>Outcome of <see cref="IAttendanceProvider.AuthenticateAsync"/>. The token is kept in memory only, never persisted.</summary>
public sealed record AuthResult(bool Succeeded, string? AccessToken = null, DateTimeOffset? ExpiresAt = null, string? Error = null);
