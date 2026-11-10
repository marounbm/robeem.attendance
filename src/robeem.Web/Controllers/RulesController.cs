using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The rules that decide how breaks are read and which findings become exceptions: seen as sentences, changed on a draft, and published as the
/// next version. A published set is never edited.
/// </summary>
public sealed class RulesController : Controller
{
    private const string StatusMessageKey = "StatusMessage";
    private const string ErrorMessageKey = "ErrorMessage";

    private readonly IRulesApiClient _rules;

    public RulesController(IRulesApiClient rules)
    {
        _rules = rules;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? scheduleId, CancellationToken cancellationToken)
    {
        var current = await _rules.GetCurrentAsync(scheduleId, cancellationToken);
        if (current is null)
        {
            return NotFound();
        }

        return View(new RulesIndexViewModel
        {
            Scopes = await _rules.ListScopesAsync(cancellationToken),
            ScheduleId = scheduleId,
            Current = current,
            HasDraft = await _rules.GetDraftAsync(scheduleId, cancellationToken) is not null,
            History = await _rules.ListHistoryAsync(scheduleId, cancellationToken),
        });
    }

    /// <summary>A set from the history, read-only.</summary>
    [HttpGet]
    public async Task<IActionResult> Version(int id, CancellationToken cancellationToken)
    {
        var set = await _rules.GetAsync(id, cancellationToken);
        return set is null ? NotFound() : View(set);
    }

    /// <summary>Starts a draft of the rules in force, or goes on with the one that exists.</summary>
    [HttpPost]
    public async Task<IActionResult> StartDraft(int? scheduleId, CancellationToken cancellationToken)
    {
        if (await _rules.GetDraftAsync(scheduleId, cancellationToken) is null)
        {
            var created = await _rules.CreateDraftAsync(scheduleId, cancellationToken);
            if (!created.Succeeded)
            {
                TempData[ErrorMessageKey] = created.MessageText;
                return RedirectToAction(nameof(Index), new { scheduleId });
            }
        }

        return RedirectToAction(nameof(Edit), new { scheduleId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int? scheduleId, CancellationToken cancellationToken)
    {
        var draft = await _rules.GetDraftAsync(scheduleId, cancellationToken);
        if (draft is null)
        {
            return RedirectToAction(nameof(Index), new { scheduleId });
        }

        return View(new RulesEditViewModel { DraftId = draft.Id!.Value, ScheduleId = scheduleId, ScopeTitle = draft.ScopeTitle, Notes = draft.Notes, Rules = draft.Rules });
    }

    /// <summary>
    /// One form, several buttons. Save and publish go to the Api; the others change the list on the page (add, remove or move a rule or
    /// a condition) and show it again, unsaved, so a person builds a rule before it is checked.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Edit(int draftId, int? scheduleId, RulesEditViewModel model, string command, CancellationToken cancellationToken)
    {
        model.DraftId = draftId;
        model.ScheduleId = scheduleId;
        model.Rules ??= [];
        Normalize(model);

        if (command is "save" or "publish")
        {
            var saved = await _rules.SaveDraftAsync(draftId, new RulesSaveRequest { Rules = model.Rules, Notes = model.Notes }, cancellationToken);
            var result = saved.Succeeded && command == "publish" ? await _rules.PublishAsync(draftId, model.Notes, cancellationToken) : saved;
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData[StatusMessageKey] = command == "publish" ? "The rules were published. Days are calculated with them from now on, and the nightly look-back brings recent days up to date." : "The draft was saved.";
                return command == "publish" ? RedirectToAction(nameof(Index), new { scheduleId }) : RedirectToAction(nameof(Edit), new { scheduleId });
            }

            foreach (var (field, message) in result.Errors)
            {
                ModelState.AddModelError(field, message);
            }

            await FillTitleAsync(model, cancellationToken);
            return View(model);
        }

        Change(model, command);
        ModelState.Clear();
        await FillTitleAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Discard(int draftId, int? scheduleId, CancellationToken cancellationToken)
    {
        var result = await _rules.DiscardDraftAsync(draftId, cancellationToken);
        TempData[result.Succeeded ? StatusMessageKey : ErrorMessageKey] = result.Succeeded ? "The draft was thrown away." : result.MessageText;
        return RedirectToAction(nameof(Index), new { scheduleId });
    }

    /// <summary>A rule's stage and finding follow from its action, and a reason belongs only to a suppression. The sentence is the server's to write.</summary>
    private static void Normalize(RulesEditViewModel model)
    {
        foreach (var rule in model.Rules)
        {
            rule.Conditions ??= [];
            rule.Stage = rule.Action == "InferBreak" ? "Interpretation" : "Exception";
            rule.Sentence = string.Empty;
            if (rule.Action == "InferBreak")
            {
                rule.Finding = null;
            }

            if (rule.Action != "SuppressException")
            {
                rule.Reason = null;
            }
        }
    }

    private static void Change(RulesEditViewModel model, string command)
    {
        var parts = (command ?? string.Empty).Split(':');
        int Index(int position) => parts.Length > position && int.TryParse(parts[position], out var value) ? value : -1;
        var rules = model.Rules;
        var i = Index(1);
        switch (parts[0])
        {
            case "add-rule":
                rules.Add(new RuleDto { Name = "New rule", Stage = "Exception", Action = "RaiseException", Finding = "NoShow", IsEnabled = true });
                break;
            case "remove-rule" when i >= 0 && i < rules.Count:
                rules.RemoveAt(i);
                break;
            case "up" when i > 0 && i < rules.Count:
                (rules[i - 1], rules[i]) = (rules[i], rules[i - 1]);
                break;
            case "down" when i >= 0 && i < rules.Count - 1:
                (rules[i + 1], rules[i]) = (rules[i], rules[i + 1]);
                break;
            case "add-condition" when i >= 0 && i < rules.Count:
                rules[i].Conditions.Add(new ConditionDto { Fact = rules[i].Action == "InferBreak" ? "GapMinutes" : "MinutesOver", Operator = "GreaterOrEqual", Value = 0 });
                break;
            case "remove-condition" when i >= 0 && i < rules.Count && Index(2) is var j && j >= 0 && j < rules[i].Conditions.Count:
                rules[i].Conditions.RemoveAt(j);
                break;
        }
    }

    private async Task FillTitleAsync(RulesEditViewModel model, CancellationToken cancellationToken) =>
        model.ScopeTitle = (await _rules.GetDraftAsync(model.ScheduleId, cancellationToken))?.ScopeTitle ?? model.ScopeTitle;
}
