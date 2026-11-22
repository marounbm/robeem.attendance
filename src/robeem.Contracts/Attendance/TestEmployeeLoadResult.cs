namespace robeem.Contracts.Attendance;

/// <summary>What loading a test employees file made.</summary>
public sealed class TestEmployeeLoadResult
{
    /// <summary>Gets the number of employees created.</summary>
    public int Created { get; init; }

    /// <summary>Gets the number of rows left alone because the tenant already has an employee with that number.</summary>
    public int Skipped { get; init; }

    /// <summary>Gets the number of employees put on a time schedule (and team).</summary>
    public int Assigned { get; init; }

    /// <summary>Gets the number of employee ids mapped to a device, so that punches with that id are found.</summary>
    public int Mapped { get; init; }

    /// <summary>Gets the names of the devices that were made because the file named them and the tenant had none (and the integration made for them, if any).</summary>
    public IReadOnlyList<string> DevicesCreated { get; init; } = [];
}
