using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// What to list from one integration's raw punches, and which page. <paramref name="Page"/> is 1-based (a page past the end answers the
/// last page); <paramref name="PageSize"/> is clamped to 1..200 (0 means the default, 20).
/// </summary>
public sealed record RawNormalizedFilter(int? BatchId, RawTransactionProcessingStatus? Status, int Page, int PageSize);
