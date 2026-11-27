namespace robeem.Contracts.Attendance;

/// <summary>What closing every ready pay period did: how many were closed, and why each of the others was not.</summary>
public sealed class CompensationCloseAllResult
{
    /// <summary>The most employees named in <see cref="Skipped"/>; the counts always cover everyone.</summary>
    public const int MaxSkippedListed = 50;

    public int Closed { get; init; }

    /// <summary>Gets the employees whose period was closed already.</summary>
    public int AlreadyClosed { get; init; }

    /// <summary>Gets the employees with no day payroll may pay for in their period: there is nothing to close.</summary>
    public int NothingToClose { get; init; }

    /// <summary>Gets the employees whose period has not ended yet.</summary>
    public int NotEnded { get; init; }

    /// <summary>Gets the employees with days that are not ready for payroll.</summary>
    public int NotReady { get; init; }

    /// <summary>Gets the employees the closing was refused for another reason.</summary>
    public int Refused { get; init; }

    /// <summary>Gets the employees that were not closed because of something to put right (not ended, not ready or refused), with why.</summary>
    public IReadOnlyList<CompensationSkippedEmployee> Skipped { get; init; } = Array.Empty<CompensationSkippedEmployee>();
}
