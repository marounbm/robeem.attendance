namespace robeem.Contracts.Attendance;

/// <summary>Which employee a vendor's employee id stands for, on one device.</summary>
public sealed class MappingItem
{
    public int Id { get; init; }

    public string ExternalEmployeeId { get; init; } = string.Empty;

    public int EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;
}
