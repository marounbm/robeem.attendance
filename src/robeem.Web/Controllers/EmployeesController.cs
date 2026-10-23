using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.HR.Employees;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

public sealed class EmployeesController : Controller
{
    private const string StatusMessageKey = "StatusMessage";

    private readonly IEmployeesApiClient _employees;
    private readonly IRecordsApiClient _records;

    public EmployeesController(IEmployeesApiClient employees, IRecordsApiClient records)
    {
        _employees = employees;
        _records = records;
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
            Managers = await _employees.ListManagerOptionsAsync(null, cancellationToken),
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
        model.Managers = await _employees.ListManagerOptionsAsync(null, cancellationToken);
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

        // Fetch recent attendance records (last 30 days)
        var from = DateTime.Today.AddDays(-30);
        var recentRecords = (await _records.ListAsync(from, DateTime.Today, id, null, false, 1, 31, cancellationToken)).Items;

        return View(new EmployeeFormViewModel
        {
            Id = id,
            Input = input,
            Schedules = await _employees.ListSchedulesAsync(cancellationToken),
            Managers = await _employees.ListManagerOptionsAsync(id, cancellationToken),
            RecentAttendanceRecords = recentRecords.OrderByDescending(r => r.AttendanceDate).ToList(),
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
        model.Managers = await _employees.ListManagerOptionsAsync(id, cancellationToken);
        model.Schedules = await _employees.ListSchedulesAsync(cancellationToken);
        return View(model);
    }

    private void AddErrors(EmployeeSaveResult result)
    {
        foreach (var (field, message) in result.Errors)
        {
            // An empty field is a model-level error (shown in the validation summary).
            var key = field.Length == 0 ? string.Empty : $"{nameof(EmployeeFormViewModel.Input)}.{field}";
            ModelState.AddModelError(key, message);
        }
    }
}
