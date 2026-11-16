using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's attendance rule endpoints (<c>api/attendance/rules</c>).</summary>
public interface IRulesApiClient
{
    Task<IReadOnlyList<RuleScopeDto>> ListScopesAsync(CancellationToken cancellationToken);

    /// <summary>Gets the rules in force for the schedule (none: the whole tenant), or <c>null</c> when it is not a scope.</summary>
    Task<RuleSetDto?> GetCurrentAsync(int? scheduleId, CancellationToken cancellationToken);

    /// <summary>Gets the rules in force as plain settings (or why they can't be), or <c>null</c> when it is not a scope.</summary>
    Task<RuleSettingsDto?> GetSettingsAsync(int? scheduleId, CancellationToken cancellationToken);

    /// <summary>Writes the settings into the standard rules and publishes the result as the next version.</summary>
    Task<ApiSaveResult> SaveSettingsAsync(RuleSettingsSaveRequest request, CancellationToken cancellationToken);

    Task<RuleSetDto?> GetDraftAsync(int? scheduleId, CancellationToken cancellationToken);

    Task<RuleSetDto?> GetAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<RuleSetSummaryDto>> ListHistoryAsync(int? scheduleId, CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateDraftAsync(int? scheduleId, CancellationToken cancellationToken);

    Task<ApiSaveResult> SaveDraftAsync(int draftId, RulesSaveRequest request, CancellationToken cancellationToken);

    Task<ApiSaveResult> PublishAsync(int draftId, string? notes, CancellationToken cancellationToken);

    /// <summary>Changes one rule of the rules in force and publishes the result as the next version.</summary>
    Task<ApiSaveResult> ChangeRuleAsync(RuleChangeRequest request, CancellationToken cancellationToken);

    Task<ApiSaveResult> DiscardDraftAsync(int draftId, CancellationToken cancellationToken);
}
