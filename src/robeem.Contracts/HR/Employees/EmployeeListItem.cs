namespace robeem.Contracts.HR.Employees;

public sealed class EmployeeListItem
{
    public int Id { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string FullName { get; init; } = string.Empty;

    public string? Department { get; init; }

    public string? JobTitle { get; init; }

    public DateTime HireDate { get; init; }

    public bool IsActive { get; init; }

    public string? TimeScheduleTitle { get; init; }

    public string? ManagerName { get; init; }
}
