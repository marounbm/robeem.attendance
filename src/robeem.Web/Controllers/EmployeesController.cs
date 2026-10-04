using Microsoft.AspNetCore.Mvc;
using robeem.HR.Employees;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

public sealed class EmployeesController : Controller
{
    private const string StatusMessageKey = "StatusMessage";

    private readonly IEmployeeService _employees;

    public EmployeesController(IEmployeeService employees)
    {
        _employees = employees;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, bool includeInactive, CancellationToken cancellationToken)
        => View(new EmployeeIndexViewModel
        {
            Employees = await _employees.ListAsync(search, includeInactive, cancellationToken),
            Search = search,
            IncludeInactive = includeInactive,
        });

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var schedules = await _employees.ListSchedulesAsync(cancellationToken);
        return View(new EmployeeFormViewModel
        {
            Input = new EmployeeInput { TimeScheduleId = schedules.FirstOrDefault()?.Id },
            Schedules = schedules,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(EmployeeFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _employees.CreateAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData[StatusMessageKey] = $"Employee {model.Input.FirstName} {model.Input.LastName} was added.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result);
        }

        model.Schedules = await _employees.ListSchedulesAsync(cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var input = await _employees.GetAsync(id, cancellationToken);
        if (input is null)
        {
            return NotFound();
        }

        return View(new EmployeeFormViewModel
        {
            Id = id,
            Input = input,
            Schedules = await _employees.ListSchedulesAsync(cancellationToken),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, EmployeeFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _employees.UpdateAsync(id, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData[StatusMessageKey] = $"Employee {model.Input.FirstName} {model.Input.LastName} was updated.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result);
        }

        model.Id = id;
        model.Schedules = await _employees.ListSchedulesAsync(cancellationToken);
        return View(model);
    }

    private void AddErrors(EmployeeSaveResult result)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError($"{nameof(EmployeeFormViewModel.Input)}.{field}", message);
        }
    }
}
