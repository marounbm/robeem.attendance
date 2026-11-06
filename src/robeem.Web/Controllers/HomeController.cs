using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

public sealed class HomeController : Controller
{
    private readonly ITenantsApiClient _tenants;

    public HomeController(ITenantsApiClient tenants)
    {
        _tenants = tenants;
    }

    /// <summary>The start page: the screens the acting roles can open. Shows the maintenance page when the database is not reachable.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            await _tenants.CheckDatabaseAsync(cancellationToken);
        }
        catch (HttpRequestException ex) when (ex is not DatabaseUnavailableException)
        {
            // The Api itself does not answer: the start page needs nothing from it, as before.
        }

        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
