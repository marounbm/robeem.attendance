using Microsoft.AspNetCore.Mvc;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Web.Controllers;

/// <summary>
/// The background jobs that normalize punches, calculate days and sync integrations: when each runs
/// and runs next, how every run ended, and Run now. HR looks; an integration administrator also runs.
/// </summary>
public sealed class JobsController : Controller
{
    private const int HistorySize = 100;

    private readonly IJobsApiClient _jobs;
    private readonly ICurrentActor _actor;

    public JobsController(IJobsApiClient jobs, ICurrentActor actor)
    {
        _jobs = jobs;
        _actor = actor;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? jobName, string? status, CancellationToken cancellationToken)
    {
        var chosen = JobsViewModel.Statuses.Contains(status ?? string.Empty) ? status! : "Any";
        return View(new JobsViewModel
        {
            Jobs = await _jobs.ListAsync(cancellationToken),
            Runs = await _jobs.ListRunsAsync(jobName, chosen == "Any" ? null : chosen, HistorySize, cancellationToken),
            JobName = string.IsNullOrWhiteSpace(jobName) ? null : jobName,
            Status = chosen,
            CanRun = _actor.IsInRole(NavigationMenu.Integration),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Run(string jobName, CancellationToken cancellationToken)
    {
        var result = await _jobs.RunAsync(jobName, cancellationToken);
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded
            ? "The job was started for this tenant. It runs in the background; the history below shows how it ends."
            : result.MessageText;
        return RedirectToAction(nameof(Index));
    }
}
