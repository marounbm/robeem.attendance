using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Which raw punches to list. <paramref name="From"/> and <paramref name="To"/> are inclusive dates of
/// when the batch that stored the punch started. <paramref name="Search"/> matches the vendor's employee,
/// device and transaction ids. <paramref name="Take"/> is clamped to 1..500 (0 means the default, 100).
/// </summary>
public sealed record RawTransactionFilter(
    int? IntegrationId,
    int? BatchId,
    RawTransactionProcessingStatus? Status,
    DateTime? From,
    DateTime? To,
    string? Search,
    int Take);
