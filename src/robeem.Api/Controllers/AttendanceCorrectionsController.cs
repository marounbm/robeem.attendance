using Microsoft.AspNetCore.Mvc;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>
/// Corrections of attendance exceptions (study §11).
/// TODO(P12): there is no authentication or role system in this solution yet (CLAUDE.md §2.8,
/// DISCOVERY_NOTES.md), so who proposes and who decides is taken from the request, and any caller
/// can act as any employee. P12 adds authentication and RBAC: the requester and the decider must
/// then come from the authenticated user, and the decider's role from their permissions. Until
/// then these endpoints must not be exposed outside Development (no tenant reaches them in
/// production anyway: tenant resolution needs a <c>tenant_id</c> claim nothing issues yet).
/// </summary>
[ApiController]
[Route("api/attendance/corrections")]
public sealed class AttendanceCorrectionsController : ControllerBase
{
    private readonly IAttendanceCorrectionService _corrections;

    public AttendanceCorrectionsController(IAttendanceCorrectionService corrections)
    {
        _corrections = corrections;
    }

    /// <summary>An employee proposes the missing punch of their own open exception.</summary>
    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Propose(ProposeCorrectionRequest request, CancellationToken cancellationToken)
    {
        var result = await _corrections.ProposeCorrectionAsync(request.ExceptionId, request.ProposedValue, request.Justification, request.RequestedByEmployeeId, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.CorrectionId!.Value })
            : Invalid(result);
    }

    /// <summary>Lists the pending corrections waiting for a manager's decision, or for HR's.</summary>
    [HttpGet("pending")]
    public async Task<IReadOnlyList<CorrectionListItem>> ListPending(CorrectionApprover awaiting, CancellationToken cancellationToken)
        => await _corrections.ListPendingAsync(awaiting, cancellationToken);

    /// <summary>A manager or HR user approves or rejects a pending correction.</summary>
    [HttpPost("{id:int}/decision")]
    public async Task<ActionResult<CorrectionDecisionResponse>> Decide(int id, DecideCorrectionRequest request, CancellationToken cancellationToken)
    {
        var result = await _corrections.DecideCorrectionAsync(id, request.DecidedByUserId, request.Approved, request.IsHrDecision, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        return result.Succeeded
            ? new CorrectionDecisionResponse
            {
                CorrectionId = id,
                Status = result.Status!.Value.ToString(),
                ExceptionStatus = result.ExceptionStatus!.Value.ToString(),
            }
            : Invalid(result);
    }

    /// <summary>A 400 ValidationProblemDetails whose error keys are request property names.</summary>
    private ActionResult Invalid(CorrectionResult result)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }

        return ValidationProblem(ModelState);
    }
}
