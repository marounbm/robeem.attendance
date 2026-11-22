namespace robeem.Contracts.Attendance;

/// <summary>What the *Load test employees* screen shows: whether the host allows loading, and what the file must look like.</summary>
public sealed class TestEmployeeSummary
{
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the host allows loading test employees (<c>Attendance:TestEmployees:Enabled</c>).</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the number of employees the tenant has now.</summary>
    public int EmployeeCount { get; init; }

    /// <summary>Gets the time zone offered for a device the file names and the tenant does not have yet.</summary>
    public string DefaultTimeZone { get; init; } = string.Empty;

    /// <summary>Gets the columns the file may have, with what each is.</summary>
    public IReadOnlyList<TestEmployeeColumn> Columns { get; init; } = [];
}
