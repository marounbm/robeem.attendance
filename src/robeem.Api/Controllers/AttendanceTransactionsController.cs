using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Api.Controllers;

/// <summary>
/// A read-only look at the raw punches (exactly what the vendor sent) and the normalized punches
/// (what they became). Every list is written to the audit log.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.TransactionViewers)]
[Route("api/attendance/transactions")]
public sealed class AttendanceTransactionsController : ControllerBase
{
    private readonly IAttendanceTransactionQuery _transactions;

    public AttendanceTransactionsController(IAttendanceTransactionQuery transactions)
    {
        _transactions = transactions;
    }

    /// <param name="integrationId">Only this integration's punches.</param>
    /// <param name="batchId">Only the punches one batch stored.</param>
    /// <param name="status">Only punches with this status: Pending, Normalized, Failed or Skipped.</param>
    /// <param name="from">First date the storing batch started, inclusive.</param>
    /// <param name="to">Last date the storing batch started, inclusive.</param>
    /// <param name="search">Part of the vendor's employee, device or transaction id.</param>
    /// <param name="take">At most this many, newest first (default 100, at most 500).</param>
    [HttpGet("raw")]
    public async Task<ActionResult<IReadOnlyList<RawTransactionListItem>>> ListRaw(int? integrationId, int? batchId, string? status, DateTime? from, DateTime? to, string? search, int take, CancellationToken cancellationToken)
    {
        if (!TryParse<RawTransactionProcessingStatus>(status, nameof(status), out var parsed))
        {
            return ValidationProblem(ModelState);
        }

        return Ok(await _transactions.ListRawAsync(new RawTransactionFilter(integrationId, batchId, parsed, from, to, search, take), cancellationToken));
    }

    /// <param name="integrationId">Only this integration's punches.</param>
    /// <param name="rawId">Only the punch made from this raw punch.</param>
    /// <param name="employee">Part of the employee's number or name.</param>
    /// <param name="eventType">Only this type: CheckIn, CheckOut, BreakOut, BreakIn, Unknown or Other.</param>
    /// <param name="duplicate">Only this duplicate status: Unique, DuplicateConfirmed or DuplicateCandidate.</param>
    /// <param name="flag">Only this flag: None, FlaggedInvalidSequence, FlaggedUnrecognizedEmployee or FlaggedUnrecognizedDevice.</param>
    /// <param name="from">First attendance date, inclusive.</param>
    /// <param name="to">Last attendance date, inclusive.</param>
    /// <param name="take">At most this many, newest first (default 100, at most 500).</param>
    [HttpGet("normalized")]
    public async Task<ActionResult<IReadOnlyList<NormalizedTransactionListItem>>> ListNormalized(int? integrationId, int? rawId, string? employee, string? eventType, string? duplicate, string? flag, DateTime? from, DateTime? to, int take, CancellationToken cancellationToken)
    {
        var valid = TryParse<NormalizedEventType>(eventType, nameof(eventType), out var parsedType);
        valid &= TryParse<DuplicateStatus>(duplicate, nameof(duplicate), out var parsedDuplicate);
        valid &= TryParse<TransactionExceptionStatus>(flag, nameof(flag), out var parsedFlag);
        if (!valid)
        {
            return ValidationProblem(ModelState);
        }

        return Ok(await _transactions.ListNormalizedAsync(new NormalizedTransactionFilter(integrationId, rawId, employee, parsedType, parsedDuplicate, parsedFlag, from, to, take), cancellationToken));
    }

    /// <summary>The integrations to filter by.</summary>
    [HttpGet("integrations")]
    public async Task<ActionResult<IReadOnlyList<TransactionIntegrationOption>>> ListIntegrations(CancellationToken cancellationToken) =>
        Ok(await _transactions.ListIntegrationsAsync(cancellationToken));

    /// <summary>Parses an optional enum name. Blank is "any" (null); a name that is not a member adds a model error and returns false.</summary>
    private bool TryParse<TEnum>(string? text, string name, out TEnum? value)
        where TEnum : struct, Enum
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (Enum.TryParse<TEnum>(text.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            value = parsed;
            return true;
        }

        ModelState.AddModelError(name, $"The {name} must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
        return false;
    }
}
