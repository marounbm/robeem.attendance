namespace robeem.Contracts.Attendance;

/// <summary>What deleting test employees and their data deleted, or, for a preview, would delete.</summary>
public sealed class TestEmployeeDeleteResult
{
    /// <summary>Gets a value indicating whether the data was deleted; false for a preview.</summary>
    public bool Deleted { get; init; }

    /// <summary>Gets the number of the file's employees the tenant has.</summary>
    public int Employees { get; init; }

    /// <summary>Gets the employee numbers of the file that the tenant has no employee for; nothing is done for them.</summary>
    public IReadOnlyList<string> NotFound { get; init; } = [];

    /// <summary>Gets how many rows of each kind go (or went).</summary>
    public IReadOnlyList<AttendanceDataCount> Counts { get; init; } = [];
}
