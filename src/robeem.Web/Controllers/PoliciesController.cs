using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// Attendance policies: the tenant-wide default, plus at most one policy per time schedule. A
/// change applies to days processed from then on; days already processed keep their result until
/// they are reprocessed.
/// </summary>
public sealed class PoliciesController : Controller
{
    private readonly IPoliciesApiClient _policies;

    public PoliciesController(IPoliciesApiClient policies)
    {
        _policies = policies;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new PoliciesIndexViewModel { Policies = await _policies.ListAsync(cancellationToken) });

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new PolicyFormViewModel();
        await FillScopesAsync(model, cancellationToken);
        model.Input.TimeScheduleId = model.DefaultExists ? model.Schedules.FirstOrDefault()?.Id : null;
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(PolicyFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _policies.CreateAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Policy '{model.Input.Title}' was created.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result);
        }

        await FillScopesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (await _policies.GetInputAsync(id, cancellationToken) is not { } input)
        {
            return NotFound();
        }

        var model = new PolicyFormViewModel { Id = id, Input = input };
        await FillScopeNameAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, PolicyFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _policies.UpdateAsync(id, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Policy '{model.Input.Title}' was saved. It applies to days processed from now on.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result);
        }

        model.Id = id;
        await FillScopeNameAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await _policies.DeleteAsync(id, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded
            ? "The policy was deleted: its schedule now follows the tenant-wide default."
            : result.MessageText;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Fills what a new policy can be for: the default when none exists, and each schedule without a policy.</summary>
    private async Task FillScopesAsync(PolicyFormViewModel model, CancellationToken cancellationToken)
    {
        var policies = await _policies.ListAsync(cancellationToken);
        var taken = policies.Where(p => p.TimeScheduleId is not null).Select(p => p.TimeScheduleId).ToHashSet();
        model.DefaultExists = policies.Any(p => p.IsTenantDefault);
        model.Schedules = (await _policies.ListScheduleOptionsAsync(cancellationToken)).Where(s => !taken.Contains(s.Id)).ToList();
    }

    private async Task FillScopeNameAsync(PolicyFormViewModel model, CancellationToken cancellationToken)
    {
        var policy = (await _policies.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == model.Id);
        model.ScopeName = policy is null or { IsTenantDefault: true } ? "The whole tenant (default)" : $"Schedule: {policy.TimeScheduleTitle}";
    }

    private void AddErrors(ApiSaveResult result)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field.Length == 0 ? string.Empty : $"{nameof(PolicyFormViewModel.Input)}.{field}", message);
        }
    }
}
