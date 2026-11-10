using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's attendance rule endpoints (<c>api/attendance/rules</c>).</summary>
public interface IRulesApiClient
{
    Task<IReadOnlyList<RuleScopeDto>> ListScopesAsync(CancellationToken cancellationToken);

    /// <summary>Gets the rules in force for the schedule (none: the whole tenant), or <c>null</c> when it is not a scope.</summary>
    Task<RuleSetDto?> GetCurrentAsync(int? scheduleId, CancellationToken cancellationToken);

    Task<RuleSetDto?> GetDraftAsync(int? scheduleId, CancellationToken cancellationToken);

    Task<RuleSetDto?> GetAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<RuleSetSummaryDto>> ListHistoryAsync(int? scheduleId, CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateDraftAsync(int? scheduleId, CancellationToken cancellationToken);

    Task<ApiSaveResult> SaveDraftAsync(int draftId, RulesSaveRequest request, CancellationToken cancellationToken);

    Task<ApiSaveResult> PublishAsync(int draftId, string? notes, CancellationToken cancellationToken);

    Task<ApiSaveResult> DiscardDraftAsync(int draftId, CancellationToken cancellationToken);
}
