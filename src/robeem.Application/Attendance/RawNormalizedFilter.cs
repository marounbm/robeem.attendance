using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>What to list from one integration's raw punches. <paramref name="Take"/> is clamped to 1..500 (0 means the default, 100).</summary>
public sealed record RawNormalizedFilter(int? BatchId, RawTransactionProcessingStatus? Status, int Take);
