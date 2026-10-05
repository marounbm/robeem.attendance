namespace robeem.Contracts.HR.Employees;

/// <summary>An employee who can be chosen as someone's manager.</summary>
public sealed class ManagerOption
{
    public int Id { get; init; }

    /// <summary>Gets the name to show: the full name with the employee number.</summary>
    public string Name { get; init; } = string.Empty;
}
