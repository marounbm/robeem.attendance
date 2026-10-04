using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using robeem.Web.Models;
using robeem.Web.Tenancy;

namespace robeem.Web.Controllers;

public sealed class HomeController : Controller
{
    [AllowWithoutTenant]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
