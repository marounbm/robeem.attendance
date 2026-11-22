namespace robeem.Contracts.HR.Employees;

/// <summary>The columns the employees list can be sorted by.</summary>
public enum EmployeeSortField
{
    /// <summary>The employee number, in natural order: shorter numbers first, so 9 comes before 10.</summary>
    Number,
    Name,
    Department,
    JobTitle,
    TimeSchedule,
    Manager,
    HireDate,

    /// <summary>Active before inactive, when ascending.</summary>
    Status,
}
