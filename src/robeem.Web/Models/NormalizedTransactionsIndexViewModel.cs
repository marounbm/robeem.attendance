using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>The normalized punches, interpreted from the raw ones, and the filter that chose them.</summary>
public sealed class NormalizedTransactionsIndexViewModel
{
    public static IReadOnlyList<string> EventTypes { get; } = ["CheckIn", "CheckOut", "BreakOut", "BreakIn", "Unknown", "Other"];

    public static IReadOnlyList<string> DuplicateStatuses { get; } = ["Unique", "DuplicateConfirmed", "DuplicateCandidate"];

    public static IReadOnlyList<string> Flags { get; } = ["None", "FlaggedInvalidSequence", "FlaggedUnrecognizedEmployee", "FlaggedUnrecognizedDevice"];

    public IReadOnlyList<NormalizedTransactionListItem> Rows { get; init; } = Array.Empty<NormalizedTransactionListItem>();

    public IReadOnlyList<TransactionIntegrationOption> Integrations { get; init; } = Array.Empty<TransactionIntegrationOption>();

    public int? IntegrationId { get; init; }

    public int? RawId { get; init; }

    public string? Employee { get; init; }

    public string? EventType { get; init; }

    public string? Duplicate { get; init; }

    public string? Flag { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public int Take { get; init; }
}
