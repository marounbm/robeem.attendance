namespace robeem.Contracts.Attendance;

/// <summary>An employee that can be chosen in an attendance screen (for example, when mapping a vendor's employee id).</summary>
public sealed class EmployeeOption
{
    public int Id { get; init; }

    /// <summary>Gets the name to show: the full name with the employee number.</summary>
    public string Name { get; init; } = string.Empty;
}
