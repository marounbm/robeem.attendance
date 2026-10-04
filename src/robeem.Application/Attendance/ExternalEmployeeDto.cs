namespace robeem.Application.Attendance;

/// <summary>An employee as the vendor describes it.</summary>
public sealed record ExternalEmployeeDto(string ExternalEmployeeId, string? DisplayName = null, string? EmployeeNumber = null);
