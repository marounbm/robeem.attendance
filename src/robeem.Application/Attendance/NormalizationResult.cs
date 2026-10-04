namespace robeem.Application.Attendance;

/// <summary>
/// What one <see cref="INormalizationService.NormalizeBatchAsync"/> run did. <see cref="Duplicates"/>
/// and <see cref="Flagged"/> are subsets of <see cref="Normalized"/>.
/// </summary>
public sealed record NormalizationResult(int Normalized, int Duplicates, int Flagged, int Failed)
{
    public int Total => Normalized + Failed;
}
