using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// The rules that decide how breaks are read and which findings become exceptions, for HR to see and change. A scope is a policy's:
/// one time schedule that has a policy of its own, or the whole tenant (<c>null</c>). The rules in force are the scope's published set,
/// else the policy's own settings written as rules. A change is made on a draft and published as the next version; a published set is
/// never edited.
/// </summary>
public interface IAttendanceRuleService
{
    /// <summary>Lists the scopes rules can be set for: the whole tenant first, then each schedule that has a policy.</summary>
    Task<IReadOnlyList<RuleScopeDto>> ListScopesAsync(CancellationToken cancellationToken);

    /// <summary>Gets the rules in force for the scope, or <c>null</c> when it is not a scope.</summary>
    Task<RuleSetDto?> GetCurrentAsync(int? timeScheduleId, CancellationToken cancellationToken);

    /// <summary>Gets the scope's draft, or <c>null</c> when there is none.</summary>
    Task<RuleSetDto?> GetDraftAsync(int? timeScheduleId, CancellationToken cancellationToken);

    /// <summary>Gets any stored set (a version in the history, or a draft) by id.</summary>
    Task<RuleSetDto?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Lists the scope's published and retired sets, latest first.</summary>
    Task<IReadOnlyList<RuleSetSummaryDto>> ListHistoryAsync(int? timeScheduleId, CancellationToken cancellationToken);

    /// <summary>Starts a draft as a copy of the rules in force. A scope has at most one draft.</summary>
    Task<SaveResult> CreateDraftAsync(int? timeScheduleId, CancellationToken cancellationToken);

    /// <summary>Replaces the rules of a draft with those sent, if they pass <see cref="RuleValidator"/>. The list order is the order the rules are looked at.</summary>
    Task<SaveResult> SaveDraftAsync(int draftId, RulesSaveRequest request, CancellationToken cancellationToken);

    /// <summary>Publishes the draft as the next version, retiring the set in force. The draft must pass <see cref="RuleValidator"/>. Audited, with any suppression and its reason.</summary>
    Task<SaveResult> PublishAsync(int draftId, string? notes, CancellationToken cancellationToken);

    /// <summary>Throws the draft away.</summary>
    Task<SaveResult> DiscardDraftAsync(int draftId, CancellationToken cancellationToken);
}
