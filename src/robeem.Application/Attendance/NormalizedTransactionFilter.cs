using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Which normalized punches to list, and which page of them. <paramref name="From"/> and <paramref name="To"/> are inclusive
/// attendance dates. <paramref name="Employee"/> matches an employee's number or name. <paramref name="Page"/> is 1-based (a page
/// past the end answers the last page); <paramref name="PageSize"/> is clamped to 1..200 (0 means the default, 20).
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
    int Page,
    int PageSize);
