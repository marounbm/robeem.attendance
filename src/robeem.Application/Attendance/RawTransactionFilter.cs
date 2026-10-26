using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Which raw punches to list, and which page of them. <paramref name="From"/> and <paramref name="To"/> are inclusive dates of
/// when the batch that stored the punch started. <paramref name="Search"/> matches the vendor's employee,
/// device and transaction ids. <paramref name="Page"/> is 1-based (a page past the end answers the last page);
/// <paramref name="PageSize"/> is clamped to 1..200 (0 means the default, 20).
/// </summary>
public sealed record RawTransactionFilter(
    int? IntegrationId,
    int? BatchId,
    RawTransactionProcessingStatus? Status,
    DateTime? From,
    DateTime? To,
    string? Search,
    int Page,
    int PageSize)
{
    /// <summary>
    /// Gets part of an employee's number or name, as on the normalized list. A raw punch belongs to an employee through the mapping of its
    /// vendor employee id on its device (the one the list shows), not through anything stored on the punch.
    /// </summary>
    public string? Employee { get; init; }
}
