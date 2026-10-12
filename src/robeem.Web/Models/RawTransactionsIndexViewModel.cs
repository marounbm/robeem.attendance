using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>The raw punches, exactly as the vendors sent them, and the filter that chose them.</summary>
public sealed class RawTransactionsIndexViewModel
{
    public static IReadOnlyList<string> Statuses { get; } = ["Pending", "Normalized", "Failed", "Skipped"];

    public IReadOnlyList<RawTransactionListItem> Rows { get; init; } = Array.Empty<RawTransactionListItem>();

    public IReadOnlyList<TransactionIntegrationOption> Integrations { get; init; } = Array.Empty<TransactionIntegrationOption>();

    public int? IntegrationId { get; init; }

    public int? BatchId { get; init; }

    public string? Status { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public string? Search { get; init; }

    public int Take { get; init; }
}
