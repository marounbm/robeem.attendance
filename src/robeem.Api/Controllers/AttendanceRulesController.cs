using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>
/// The rules that decide how breaks are read and which findings become exceptions. HR sees the rules in force, changes them on a draft,
/// and publishes the draft as the next version; a published set is never edited.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.HRAdministrator)]
[Route("api/attendance/rules")]
public sealed class AttendanceRulesController : ControllerBase
{
    private readonly IAttendanceRuleService _rules;

    public AttendanceRulesController(IAttendanceRuleService rules)
    {
        _rules = rules;
    }

    /// <summary>The scopes rules can be set for: the whole tenant, then each schedule that has a policy.</summary>
    [HttpGet("scopes")]
    public async Task<IReadOnlyList<RuleScopeDto>> Scopes(CancellationToken cancellationToken) =>
        await _rules.ListScopesAsync(cancellationToken);

    /// <param name="scheduleId">The schedule; none is the whole tenant.</param>
    [HttpGet("current")]
    public async Task<ActionResult<RuleSetDto>> Current(int? scheduleId, CancellationToken cancellationToken) =>
        await _rules.GetCurrentAsync(scheduleId, cancellationToken) is { } set ? set : NotFound();

    [HttpGet("draft")]
    public async Task<ActionResult<RuleSetDto>> Draft(int? scheduleId, CancellationToken cancellationToken) =>
        await _rules.GetDraftAsync(scheduleId, cancellationToken) is { } set ? set : NotFound();

    [HttpGet("history")]
    public async Task<IReadOnlyList<RuleSetSummaryDto>> History(int? scheduleId, CancellationToken cancellationToken) =>
        await _rules.ListHistoryAsync(scheduleId, cancellationToken);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RuleSetDto>> Get(int id, CancellationToken cancellationToken) =>
        await _rules.GetAsync(id, cancellationToken) is { } set ? set : NotFound();

    [HttpPost("drafts")]
    public async Task<ActionResult<CreatedResponse>> CreateDraft(int? scheduleId, CancellationToken cancellationToken)
    {
        var result = await _rules.CreateDraftAsync(scheduleId, cancellationToken);
        return result.Succeeded ? CreatedAtAction(nameof(Get), new { id = result.Id!.Value }, new CreatedResponse { Id = result.Id.Value }) : Failure(result);
    }

    [HttpPut("drafts/{id:int}")]
    public async Task<IActionResult> SaveDraft(int id, RulesSaveRequest request, CancellationToken cancellationToken)
    {
        var result = await _rules.SaveDraftAsync(id, request, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    [HttpPost("drafts/{id:int}/publish")]
    public async Task<IActionResult> Publish(int id, PublishRuleSetRequest request, CancellationToken cancellationToken)
    {
        var result = await _rules.PublishAsync(id, request.Notes, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    [HttpDelete("drafts/{id:int}")]
    public async Task<IActionResult> DiscardDraft(int id, CancellationToken cancellationToken)
    {
        var result = await _rules.DiscardDraftAsync(id, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    private ActionResult Failure(SaveResult result)
    {
        if (result.NotFound)
        {
            return NotFound();
        }

        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }

        return ValidationProblem(ModelState);
    }
}
