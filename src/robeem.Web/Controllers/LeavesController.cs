using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.HR.Leaves;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>Employees' leaves: a paged list, and add, change and remove. A leave HR saves counts as approved.</summary>
public sealed class LeavesController : Controller
{
    private const string StatusMessageKey = "StatusMessage";
    private const string ErrorMessageKey = "ErrorMessage";

    private readonly ILeavesApiClient _leaves;
    private readonly IEmployeesApiClient _employees;

    public LeavesController(ILeavesApiClient leaves, IEmployeesApiClient employees)
    {
        _leaves = leaves;
        _employees = employees;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? employeeId, string? search, DateTime? from, DateTime? to, string? type, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = LeavesIndexViewModel.PageSizes.Contains(pageSize) ? pageSize : LeavesIndexViewModel.DefaultPageSize;
        var leaveType = type is not null && LeavesIndexViewModel.LeaveTypes.Contains(type) ? type : null;
        var result = await _leaves.ListAsync(employeeId, search, from, to, leaveType, Math.Max(page, 1), size, cancellationToken);
        return View(new LeavesIndexViewModel
        {
            Leaves = result.Items,
            EmployeeId = employeeId,
            Search = search,
            From = from,
            To = to,
            LeaveType = leaveType,
            Page = result.Page,
            PageSize = size,
            Total = result.Total,
        });
    }

    /// <summary>With an employee, the form; without one, a search box to find them.</summary>
    [HttpGet]
    public async Task<IActionResult> Create(int? employeeId, string? search, CancellationToken cancellationToken)
    {
        if (employeeId is { } id)
        {
            var employee = await _employees.GetAsync(id, cancellationToken);
            if (employee is null)
            {
                return NotFound();
            }

            return View(new LeaveFormViewModel
            {
                Input = new LeaveInput { EmployeeId = id },
                EmployeeNumber = employee.EmployeeNumber,
                EmployeeName = $"{employee.FirstName} {employee.LastName}",
            });
        }

        return View(new LeaveFormViewModel
        {
            Search = search,
            Matches = string.IsNullOrWhiteSpace(search) ? [] : await _employees.LookupAsync(search, cancellationToken),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(LeaveFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _leaves.CreateAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData[StatusMessageKey] = "The leave was added.";
                return RedirectToAction(nameof(Index), new { employeeId = model.Input.EmployeeId });
            }

            AddErrors(result);
        }

        await FillEmployeeAsync(model, cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var leave = await _leaves.GetAsync(id, cancellationToken);
        if (leave is null)
        {
            return NotFound();
        }

        return View(new LeaveFormViewModel
        {
            Id = id,
            EmployeeNumber = leave.EmployeeNumber,
            EmployeeName = leave.EmployeeName,
            Input = new LeaveInput
            {
                EmployeeId = leave.EmployeeId,
                LeaveType = leave.LeaveType,
                StartDate = leave.StartDate,
                EndDate = leave.EndDate,
                IsPartDay = leave.IsPartDay,
                StartTime = leave.IsPartDay ? leave.StartTime : null,
                EndTime = leave.IsPartDay ? leave.EndTime : null,
                Notes = leave.Notes,
            },
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, LeaveFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _leaves.UpdateAsync(id, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData[StatusMessageKey] = "The leave was updated.";
                return RedirectToAction(nameof(Index), new { employeeId = model.Input.EmployeeId });
            }

            AddErrors(result);
        }

        model.Id = id;
        await FillEmployeeAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Remove(int id, int? employeeId, CancellationToken cancellationToken)
    {
        var result = await _leaves.DeleteAsync(id, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? StatusMessageKey : ErrorMessageKey] = result.Succeeded ? "The leave was removed." : result.MessageText;
        return RedirectToAction(nameof(Index), new { employeeId });
    }

    /// <summary>The employee's number and name are not posted back, so they are read again to redisplay the form.</summary>
    private async Task FillEmployeeAsync(LeaveFormViewModel model, CancellationToken cancellationToken)
    {
        var employee = model.Input.EmployeeId > 0 ? await _employees.GetAsync(model.Input.EmployeeId, cancellationToken) : null;
        if (employee is not null)
        {
            model.EmployeeNumber = employee.EmployeeNumber;
            model.EmployeeName = $"{employee.FirstName} {employee.LastName}";
        }
    }

    private void AddErrors(ApiSaveResult result)
    {
        foreach (var (field, message) in result.Errors)
        {
            // An empty field is a model-level error (shown in the validation summary).
            ModelState.AddModelError(field.Length == 0 ? string.Empty : $"{nameof(LeaveFormViewModel.Input)}.{field}", message);
        }
    }
}
