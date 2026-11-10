namespace robeem.Contracts.HR.Employees;

/// <summary>An employee found by a search box: enough to show and to pick.</summary>
public sealed class EmployeeLookupItem
{
    public int Id { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}
