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
    private const string ChangedMessage = "The rule was changed and the rules were published as a new version. Days are calculated with them from now on.";
    private const string StaleMessage = "The rules were changed since that page was shown. Here they are as they are now.";

    private readonly IRulesApiClient _rules;

    public RulesController(IRulesApiClient rules)
    {
        _rules = rules;
    }

    /// <summary>The rules as plain settings: a switch and a number for each thing HR usually changes. Saving publishes them as the next version.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(int? scheduleId, CancellationToken cancellationToken)
    {
        var settings = await _rules.GetSettingsAsync(scheduleId, cancellationToken);
        return settings is null ? NotFound() : View(await SettingsModelAsync(settings, settings.Settings, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> SaveSettings(int? scheduleId, int version, RulesSettingsViewModel model, CancellationToken cancellationToken)
    {
        var current = await _rules.GetSettingsAsync(scheduleId, cancellationToken);
        if (current is null)
        {
            return NotFound();
        }

        if (current.IsStandard && current.Version == version && current.Settings == model.Settings)
        {
            TempData[StatusMessageKey] = "Nothing was changed.";
            return RedirectToAction(nameof(Index), new { scheduleId });
        }

        var result = await _rules.SaveSettingsAsync(new RuleSettingsSaveRequest { TimeScheduleId = scheduleId, ExpectedVersion = version, Settings = model.Settings }, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            TempData[StatusMessageKey] = "The settings were saved and published as a new version. Days are calculated with them from now on.";
            return RedirectToAction(nameof(Index), new { scheduleId });
        }

        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }

        // Shown again as the person left it, on the version the rules are at now.
        return View(nameof(Index), await SettingsModelAsync(current, model.Settings, cancellationToken));
    }

    /// <summary>The rules one by one, with what each does in words: change one, move it, switch it off, or make a draft of several changes.</summary>
    [HttpGet]
    public async Task<IActionResult> Advanced(int? scheduleId, CancellationToken cancellationToken)
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
                return RedirectToAction(nameof(Advanced), new { scheduleId });
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
            return RedirectToAction(nameof(Advanced), new { scheduleId });
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
                return command == "publish" ? RedirectToAction(nameof(Advanced), new { scheduleId }) : RedirectToAction(nameof(Edit), new { scheduleId });
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

    /// <summary>
    /// Switches one rule on or off, deletes it or moves it, straight from the grid. A published set is never edited, so the change is
    /// published at once as the next version.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Manage(int? scheduleId, int version, int position, string operation, CancellationToken cancellationToken)
    {
        var result = await _rules.ChangeRuleAsync(
            new RuleChangeRequest { TimeScheduleId = scheduleId, ExpectedVersion = version, Position = position, Operation = operation },
            cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? StatusMessageKey : ErrorMessageKey] = result.Succeeded ? ChangedMessage : result.MessageText;
        return RedirectToAction(nameof(Advanced), new { scheduleId });
    }

    /// <summary>One rule of the rules in force, on a page of its own.</summary>
    [HttpGet]
    public async Task<IActionResult> EditRule(int? scheduleId, int position, int version, CancellationToken cancellationToken)
    {
        var current = await _rules.GetCurrentAsync(scheduleId, cancellationToken);
        if (current is null || position < 0 || position >= current.Rules.Count)
        {
            return NotFound();
        }

        if (current.Version != version)
        {
            TempData[ErrorMessageKey] = StaleMessage;
            return RedirectToAction(nameof(Advanced), new { scheduleId });
        }

        return View(new RulesEditViewModel { ScheduleId = scheduleId, ScopeTitle = current.ScopeTitle, Position = position, ExpectedVersion = version, Rules = [current.Rules[position]] });
    }

    /// <summary>Saving publishes the rule as the next version; the other buttons change the rule on the page (add or remove a condition) and show it again, unsaved.</summary>
    [HttpPost]
    public async Task<IActionResult> EditRule(int? scheduleId, int position, int version, RulesEditViewModel model, string command, CancellationToken cancellationToken)
    {
        model.ScheduleId = scheduleId;
        model.Position = position;
        model.ExpectedVersion = version;
        if (model.Rules is not { Count: 1 })
        {
            return BadRequest();
        }

        Normalize(model);
        if (command == "save")
        {
            var result = await _rules.ChangeRuleAsync(
                new RuleChangeRequest { TimeScheduleId = scheduleId, ExpectedVersion = version, Position = position, Operation = RuleChangeOperations.Replace, Rule = model.Rules[0], Notes = model.Notes },
                cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData[StatusMessageKey] = ChangedMessage;
                return RedirectToAction(nameof(Advanced), new { scheduleId });
            }

            foreach (var (field, message) in result.Errors)
            {
                ModelState.AddModelError(field, message);
            }
        }
        else
        {
            Change(model, command);
            ModelState.Clear();
        }

        model.ScopeTitle = (await _rules.GetCurrentAsync(scheduleId, cancellationToken))?.ScopeTitle ?? model.ScopeTitle;
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Discard(int draftId, int? scheduleId, CancellationToken cancellationToken)
    {
        var result = await _rules.DiscardDraftAsync(draftId, cancellationToken);
        TempData[result.Succeeded ? StatusMessageKey : ErrorMessageKey] = result.Succeeded ? "The draft was thrown away." : result.MessageText;
        return RedirectToAction(nameof(Advanced), new { scheduleId });
    }

    /// <summary>A rule's stage and finding follow from its action, and a reason belongs only to a suppression. The sentence is the server's to write.</summary>
    private static void Normalize(RulesEditViewModel model)
    {
        foreach (var rule in model.Rules)
        {
            rule.Conditions ??= [];
            var interpretation = RulesEditViewModel.InterpretationActions.Contains(rule.Action);
            rule.Stage = interpretation ? "Interpretation" : "Exception";
            rule.Sentence = string.Empty;
            if (interpretation)
            {
                rule.Finding = null;
            }

            if (rule.Action != "SuppressException")
            {
                rule.Reason = null;
            }
        }
    }

    /// <summary>The fact a new condition looks at to begin with: the gap, the break, or the finding, by what the rule does.</summary>
    private static string DefaultFact(string action) => action switch
    {
        "InferBreak" => "GapMinutes",
        "IgnoreShortBreak" => "BreakMinutes",
        "LoneCheckOutIsCheckIn" or "LoneCheckInIsCheckOut" or "KeepFirstMorningPunch" or "KeepFirstEveningPunch" => "PunchOffsetMinutes",
        _ => "MinutesOver",
    };

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
                rules[i].Conditions.Add(new ConditionDto { Fact = DefaultFact(rules[i].Action), Operator = "GreaterOrEqual", Value = 0 });
                break;
            case "remove-condition" when i >= 0 && i < rules.Count && Index(2) is var j && j >= 0 && j < rules[i].Conditions.Count:
                rules[i].Conditions.RemoveAt(j);
                break;
        }
    }

    private async Task<RulesSettingsViewModel> SettingsModelAsync(RuleSettingsDto current, RuleSettings shown, CancellationToken cancellationToken)
    {
        var scopes = await _rules.ListScopesAsync(cancellationToken);
        return new RulesSettingsViewModel
        {
            Scopes = scopes,
            ScheduleId = current.TimeScheduleId,
            ScopeTitle = current.ScopeTitle,
            Version = current.Version,
            IsStandard = current.IsStandard,
            NotStandardReason = current.NotStandardReason,
            HasDraft = scopes.Any(s => s.TimeScheduleId == current.TimeScheduleId && s.HasDraft),
            Settings = shown,
        };
    }

    private async Task FillTitleAsync(RulesEditViewModel model, CancellationToken cancellationToken) =>
        model.ScopeTitle = (await _rules.GetDraftAsync(model.ScheduleId, cancellationToken))?.ScopeTitle ?? model.ScopeTitle;
}
