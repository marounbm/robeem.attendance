using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>How often each employee is paid: the default pay cycle, and an employee's own. Their pay periods follow from it.</summary>
public sealed class PayCyclesController : Controller
{
    private readonly IPayCyclesApiClient _cycles;

    public PayCyclesController(IPayCyclesApiClient cycles)
    {
        _cycles = cycles;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = PayCyclesIndexViewModel.PageSizes.Contains(pageSize) ? pageSize : PayCyclesIndexViewModel.DefaultPageSize;
        var result = await _cycles.ListAsync(search, page <= 0 ? 1 : page, size, cancellationToken);
        return View(new PayCyclesIndexViewModel
        {
            Default = await _cycles.GetDefaultAsync(cancellationToken),
            Employees = result.Items,
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            Page = result.Page,
            PageSize = size,
            Total = result.Total,
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveDefault(PayCycleInput input, CancellationToken cancellationToken)
    {
        var result = await _cycles.SaveDefaultAsync(input, cancellationToken);
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded ? "The default pay cycle was saved." : result.MessageText;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var employee = (await _cycles.ListAsync(null, 1, 200, cancellationToken)).Items.FirstOrDefault(e => e.EmployeeId == id);
        var own = await _cycles.GetEmployeeAsync(id, cancellationToken);
        return View(new PayCycleEditViewModel
        {
            EmployeeId = id,
            Input = own ?? new PayCycleInput(),
            HasOwn = own is not null,
            EmployeeLabel = employee is null ? $"Employee {id}" : $"{employee.EmployeeName} ({employee.EmployeeNumber})",
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, PayCycleInput input, CancellationToken cancellationToken)
    {
        var result = await _cycles.SaveEmployeeAsync(id, input, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            TempData["StatusMessage"] = "The employee's pay cycle was saved.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }

        return View(new PayCycleEditViewModel { EmployeeId = id, Input = input, HasOwn = await _cycles.GetEmployeeAsync(id, cancellationToken) is not null, EmployeeLabel = $"Employee {id}" });
    }

    [HttpPost]
    public async Task<IActionResult> Remove(int id, CancellationToken cancellationToken)
    {
        var result = await _cycles.RemoveEmployeeAsync(id, cancellationToken);
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded ? "The employee's own pay cycle was removed: the default applies to them." : "The employee has no pay cycle of their own.";
        return RedirectToAction(nameof(Index));
    }
}
