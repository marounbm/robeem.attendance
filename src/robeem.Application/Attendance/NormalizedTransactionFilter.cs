using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Which normalized punches to list. <paramref name="From"/> and <paramref name="To"/> are inclusive
/// attendance dates. <paramref name="Employee"/> matches an employee's number or name.
/// <paramref name="Take"/> is clamped to 1..500 (0 means the default, 100).
/// </summary>
public sealed record NormalizedTransactionFilter(
    int? IntegrationId,
    int? RawId,
    string? Employee,
    NormalizedEventType? EventType,
    DuplicateStatus? Duplicate,
    TransactionExceptionStatus? Flag,
    DateTime? From,
    DateTime? To,
    int Take);
