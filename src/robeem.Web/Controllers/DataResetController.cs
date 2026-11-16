using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;

namespace robeem.Web.Controllers;

/// <summary>
/// Deletes all attendance data of the current tenant so that it can be imported again. For integration administrators, where the host turns it on;
/// the tenant id is typed to confirm. The Api decides and audits; this only asks and tells.
/// </summary>
public sealed class DataResetController : Controller
{
    private readonly IDataResetApiClient _reset;

    public DataResetController(IDataResetApiClient reset)
    {
        _reset = reset;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await _reset.GetSummaryAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Reset(string confirmation, CancellationToken cancellationToken)
    {
        var result = await _reset.ResetAsync(confirmation, cancellationToken);
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded
            ? "All attendance data was deleted: " + string.Join(", ", result.Value!.Where(d => d.Count > 0).Select(d => $"{d.Count} {d.Name.ToLowerInvariant()}")) + ". You can import again."
            : result.MessageText;
        return RedirectToAction(nameof(Index));
    }
}
