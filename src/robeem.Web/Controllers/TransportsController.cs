using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The names for the six ways attendance reaches robeem. How a transport behaves is code, so it can be renamed or
/// switched off for new integrations, but not added.
/// </summary>
public sealed class TransportsController : Controller
{
    private readonly ITransportsApiClient _transports;

    public TransportsController(ITransportsApiClient transports)
    {
        _transports = transports;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new TransportsIndexViewModel { Transports = await _transports.ListAsync(cancellationToken) });

    [HttpPost]
    public async Task<IActionResult> Update(int id, TransportInput input, CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid
            ? await _transports.UpdateAsync(id, input, cancellationToken)
            : ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "Give the transport a name." });
        if (result.NotFound)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded ? $"'{input.DisplayName}' was saved." : result.MessageText;
        return RedirectToAction(nameof(Index));
    }
}
