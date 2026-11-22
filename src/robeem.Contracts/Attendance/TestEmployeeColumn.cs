namespace robeem.Contracts.Attendance;

/// <summary>A column of the test employees file.</summary>
public sealed class TestEmployeeColumn
{
    public string Name { get; init; } = string.Empty;

    public bool Required { get; init; }

    public string Description { get; init; } = string.Empty;
}
