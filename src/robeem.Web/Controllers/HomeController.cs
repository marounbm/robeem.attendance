using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

public sealed class HomeController : Controller
{
    /// <summary>The start page: the screens the acting roles can open.</summary>
    [HttpGet]
    public IActionResult Index() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
