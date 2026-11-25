using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceRuleService : IAttendanceRuleService
{
    private const string TenantTitle = "The whole company (the default policy)";

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;

    public AttendanceRuleService(IRobeemDbContext db, IAttendanceAuditTrail audit, ICurrentUser user, TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _user = user;
        _clock = clock;
    }

    public async Task<IReadOnlyList<RuleScopeDto>> ListScopesAsync(CancellationToken cancellationToken)
    {
        var scheduleIds = await _db.AttendancePolicies.AsNoTracking().Where(p => p.TimeScheduleId != null).Select(p => p.TimeScheduleId!.Value).ToListAsync(cancellationToken);
        var titles = await _db.TimeSchedules.AsNoTracking().Where(s => scheduleIds.Contains(s.Id)).Select(s => new { s.Id, s.Title }).ToListAsync(cancellationToken);
        var sets = await _db.AttendanceRuleSets.AsNoTracking()
            .Where(s => s.Status == RuleSetStatus.Published || s.Status == RuleSetStatus.Draft)
            .Select(s => new { s.TimeScheduleId, s.Status })
            .ToListAsync(cancellationToken);

        RuleScopeDto Scope(int? id, string title) => new()
        {
            TimeScheduleId = id,
            Title = title,
            HasPublished = sets.Any(s => s.TimeScheduleId == id && s.Status == RuleSetStatus.Published),
            HasDraft = sets.Any(s => s.TimeScheduleId == id && s.Status == RuleSetStatus.Draft),
        };

        return [Scope(null, TenantTitle), .. titles.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).Select(t => Scope(t.Id, t.Title))];
    }

    public async Task<RuleSetDto?> GetCurrentAsync(int? timeScheduleId, CancellationToken cancellationToken)
    {
        var title = await ScopeTitleAsync(timeScheduleId, cancellationToken);
        if (title is null)
        {
            return null;
        }

        var published = await LoadSetsQuery()
            .Where(s => s.Status == RuleSetStatus.Published && s.TimeScheduleId == timeScheduleId)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (published is not null)
        {
            return ToDto(published, title, await PolicyOfAsync(timeScheduleId, cancellationToken));
        }

        // Nothing is published: the rules are the policy's own settings.
        var policy = await PolicyOfAsync(timeScheduleId, cancellationToken);
        var snapshot = RuleSetSnapshot.FromPolicy(policy);
        return new RuleSetDto
        {
            TimeScheduleId = timeScheduleId,
            ScopeTitle = title,
            Version = 0,
            Status = "Policy",
            Rules = snapshot.Rules.Select(ToDto).ToList(),
        };
    }

    public async Task<RuleSetDto?> GetDraftAsync(int? timeScheduleId, CancellationToken cancellationToken)
    {
        var title = await ScopeTitleAsync(timeScheduleId, cancellationToken);
        var draft = title is null ? null : await LoadSetsQuery().FirstOrDefaultAsync(s => s.Status == RuleSetStatus.Draft && s.TimeScheduleId == timeScheduleId, cancellationToken);
        return draft is null ? null : ToDto(draft, title!, await PolicyOfAsync(timeScheduleId, cancellationToken));
    }

    public async Task<RuleSetDto?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var set = await LoadSetsQuery().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return set is null ? null : ToDto(set, await ScopeTitleAsync(set.TimeScheduleId, cancellationToken) ?? TenantTitle, await PolicyOfAsync(set.TimeScheduleId, cancellationToken));
    }

    public async Task<IReadOnlyList<RuleSetSummaryDto>> ListHistoryAsync(int? timeScheduleId, CancellationToken cancellationToken)
    {
        var sets = await _db.AttendanceRuleSets.AsNoTracking()
            .Where(s => s.TimeScheduleId == timeScheduleId && s.Status != RuleSetStatus.Draft)
            .OrderByDescending(s => s.Version)
            .Select(s => new { s.Id, s.Version, s.Status, s.PublishedAt, s.PublishedById, s.Notes, Rules = s.Rules.Count })
            .ToListAsync(cancellationToken);
        var ids = sets.Where(s => s.PublishedById != null).Select(s => s.PublishedById!.Value).Distinct().ToList();
        var names = (await _db.Profiles.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.FirstName, p.LastName }).ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, p => $"{p.FirstName} {p.LastName}".Trim());

        return sets.Select(s => new RuleSetSummaryDto
        {
            Id = s.Id,
            Version = s.Version,
            Status = s.Status.ToString(),
            PublishedAt = s.PublishedAt,
            PublishedById = s.PublishedById,
            PublishedByName = s.PublishedById is { } by && names.TryGetValue(by, out var name) ? name : null,
            RuleCount = s.Rules,
            Notes = s.Notes,
        }).ToList();
    }

    public async Task<SaveResult> CreateDraftAsync(int? timeScheduleId, CancellationToken cancellationToken)
    {
        if (await ScopeTitleAsync(timeScheduleId, cancellationToken) is null)
        {
            return SaveResult.Invalid(nameof(RuleScopeDto.TimeScheduleId), "Rules can be set for the whole company, or for a schedule that has a policy of its own.");
        }

        if (await _db.AttendanceRuleSets.AnyAsync(s => s.Status == RuleSetStatus.Draft && s.TimeScheduleId == timeScheduleId, cancellationToken))
        {
            return SaveResult.Invalid("id", "There is a draft already. Finish it or throw it away first.");
        }

        var current = (await GetCurrentAsync(timeScheduleId, cancellationToken))!;
        var draft = new AttendanceRuleSet { TimeScheduleId = timeScheduleId, Status = RuleSetStatus.Draft, Version = 0, Title = "Draft rules", ThresholdsInRules = true, ShortHoursInRules = true, AutoFixesInRules = true, BreakFixesInRules = true, LateEarlyInRules = true, OvertimeInRules = true, KeepLastInRules = true, PunchPairsInRules = true, CheckPairsInRules = true, RepeatsInRules = true };
        _db.AttendanceRuleSets.Add(draft);
        AddRules(draft, current.Rules);
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(draft.Id);
    }

    public async Task<SaveResult> SaveDraftAsync(int draftId, RulesSaveRequest request, CancellationToken cancellationToken)
    {
        var draft = await LoadSetsQuery().FirstOrDefaultAsync(s => s.Id == draftId, cancellationToken);
        if (draft is null)
        {
            return SaveResult.Missing();
        }

        if (draft.Status != RuleSetStatus.Draft)
        {
            return SaveResult.Invalid("id", "A published set is never edited. Start a new draft from it.");
        }

        var errors = RuleValidator.Validate(request.Rules);
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is { Length: > 1000 })
        {
            errors[nameof(RulesSaveRequest.Notes)] = "The notes can be at most 1000 characters.";
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        _db.AttendanceRuleConditions.RemoveRange(draft.Rules.SelectMany(r => r.Conditions));
        _db.AttendanceRules.RemoveRange(draft.Rules);
        draft.Rules.Clear();
        draft.Notes = notes;

        // What was sent was read from the draft with its thresholds written in, so the draft keeps them from now on.
        draft.ThresholdsInRules = true;
        draft.ShortHoursInRules = true;
        draft.AutoFixesInRules = true;
        draft.BreakFixesInRules = true;
        draft.LateEarlyInRules = true;
        draft.OvertimeInRules = true;
        draft.KeepLastInRules = true;
        draft.PunchPairsInRules = true;
        draft.CheckPairsInRules = true;
        draft.RepeatsInRules = true;
        AddRules(draft, request.Rules);
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(draft.Id);
    }

    public async Task<SaveResult> PublishAsync(int draftId, string? notes, CancellationToken cancellationToken)
    {
        var draft = await LoadSetsQuery().FirstOrDefaultAsync(s => s.Id == draftId, cancellationToken);
        if (draft is null)
        {
            return SaveResult.Missing();
        }

        if (draft.Status != RuleSetStatus.Draft)
        {
            return SaveResult.Invalid("id", "Only a draft can be published.");
        }

        // A draft started before the thresholds moved into the rules has none written in; publishing it as it is would drop them.
        if (!draft.ThresholdsInRules)
        {
            return SaveResult.Invalid("id", "This draft was started before the break and no-show thresholds became conditions of the rules. Open it and save it again, then publish.");
        }

        var title = await ScopeTitleAsync(draft.TimeScheduleId, cancellationToken) ?? TenantTitle;
        var dto = ToDto(draft, title, await PolicyOfAsync(draft.TimeScheduleId, cancellationToken));
        var errors = RuleValidator.Validate(dto.Rules);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        await PublishSetAsync(draft, dto.Rules, title, notes, null, cancellationToken);
        return SaveResult.Success(draft.Id);
    }

    public async Task<RuleSettingsDto?> GetSettingsAsync(int? timeScheduleId, CancellationToken cancellationToken)
    {
        if (await GetCurrentAsync(timeScheduleId, cancellationToken) is not { } current)
        {
            return null;
        }

        var settings = RuleSettingsMapper.TryRead(current.Rules, out var reason);
        return new RuleSettingsDto
        {
            TimeScheduleId = timeScheduleId,
            ScopeTitle = current.ScopeTitle,
            Version = current.Version,
            IsStandard = settings is not null,
            NotStandardReason = reason,
            Settings = settings ?? new RuleSettings(),
        };
    }

    public async Task<SaveResult> SaveSettingsAsync(RuleSettingsSaveRequest request, CancellationToken cancellationToken)
    {
        var errors = RuleSettingsMapper.Validate(request.Settings);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var (refused, title, current) = await ReadyToChangeAsync(request.TimeScheduleId, request.ExpectedVersion, cancellationToken);
        if (refused is not null)
        {
            return refused;
        }

        var before = RuleSettingsMapper.TryRead(current!.Rules, out var reason);
        if (before is null)
        {
            return SaveResult.Invalid("id", reason!);
        }

        var change = RuleSettingsMapper.Describe(before, request.Settings);
        if (change.Length == 0)
        {
            return SaveResult.Invalid("id", "Nothing was changed.");
        }

        var rules = RuleSettingsMapper.Apply(current.Rules, request.Settings)!;
        var invalid = RuleValidator.Validate(rules);
        if (invalid.Count > 0)
        {
            return SaveResult.Invalid(invalid);
        }

        var set = new AttendanceRuleSet { TimeScheduleId = request.TimeScheduleId, Status = RuleSetStatus.Draft, Version = 0, Title = "Draft rules", ThresholdsInRules = true, ShortHoursInRules = true, AutoFixesInRules = true, BreakFixesInRules = true, LateEarlyInRules = true, OvertimeInRules = true, KeepLastInRules = true, PunchPairsInRules = true, CheckPairsInRules = true, RepeatsInRules = true };
        _db.AttendanceRuleSets.Add(set);
        AddRules(set, rules);
        await PublishSetAsync(set, rules, title!, change, change, cancellationToken);
        return SaveResult.Success(set.Id);
    }

    public async Task<SaveResult> ChangeRuleAsync(RuleChangeRequest request, CancellationToken cancellationToken)
    {
        var (refused, title, current) = await ReadyToChangeAsync(request.TimeScheduleId, request.ExpectedVersion, cancellationToken);
        if (refused is not null)
        {
            return refused;
        }

        var rules = current!.Rules;
        var position = request.Position;
        if (position < 0 || position >= rules.Count)
        {
            return SaveResult.Invalid("id", "There is no such rule.");
        }

        var rule = rules[position];
        var label = $"rule {position + 1} \"{rule.Name}\"";
        string change;
        switch (request.Operation)
        {
            case RuleChangeOperations.Replace:
                if (request.Rule is null)
                {
                    return SaveResult.Invalid(nameof(RuleChangeRequest.Rule), "Send the rule.");
                }

                // Validated on its own, so an error is keyed Rules[0].Field whichever place the rule has.
                var errors = RuleValidator.Validate([request.Rule]);
                if (errors.Count > 0)
                {
                    return SaveResult.Invalid(errors);
                }

                rules[position] = request.Rule;
                change = $"Changed {label}.";
                break;
            case RuleChangeOperations.Enable or RuleChangeOperations.Disable:
                var enable = request.Operation == RuleChangeOperations.Enable;
                if (rule.IsEnabled == enable)
                {
                    return SaveResult.Invalid("id", enable ? "The rule is on already." : "The rule is off already.");
                }

                rule.IsEnabled = enable;
                change = $"{(enable ? "Switched on" : "Switched off")} {label}.";
                break;
            case RuleChangeOperations.Delete:
                rules.RemoveAt(position);
                change = $"Deleted {label}.";
                break;
            case RuleChangeOperations.MoveUp or RuleChangeOperations.MoveDown:
                var up = request.Operation == RuleChangeOperations.MoveUp;
                var other = up ? position - 1 : position + 1;
                if (other < 0 || other >= rules.Count)
                {
                    return SaveResult.Invalid("id", up ? "The rule is the first already." : "The rule is the last already.");
                }

                (rules[other], rules[position]) = (rules[position], rules[other]);
                change = $"Moved {label} {(up ? "earlier" : "later")}.";
                break;
            default:
                return SaveResult.Invalid(nameof(RuleChangeRequest.Operation), $"The operation must be one of: {string.Join(", ", RuleChangeOperations.Replace, RuleChangeOperations.Enable, RuleChangeOperations.Disable, RuleChangeOperations.Delete, RuleChangeOperations.MoveUp, RuleChangeOperations.MoveDown)}.");
        }

        var set = new AttendanceRuleSet { TimeScheduleId = request.TimeScheduleId, Status = RuleSetStatus.Draft, Version = 0, Title = "Draft rules", ThresholdsInRules = true, ShortHoursInRules = true, AutoFixesInRules = true, BreakFixesInRules = true, LateEarlyInRules = true, OvertimeInRules = true, KeepLastInRules = true, PunchPairsInRules = true, CheckPairsInRules = true, RepeatsInRules = true };
        _db.AttendanceRuleSets.Add(set);
        AddRules(set, rules);
        await PublishSetAsync(set, rules, title!, string.IsNullOrWhiteSpace(request.Notes) ? change : request.Notes, change, cancellationToken);
        return SaveResult.Success(set.Id);
    }

    public async Task<SaveResult> DiscardDraftAsync(int draftId, CancellationToken cancellationToken)
    {
        var draft = await LoadSetsQuery().FirstOrDefaultAsync(s => s.Id == draftId, cancellationToken);
        if (draft is null)
        {
            return SaveResult.Missing();
        }

        if (draft.Status != RuleSetStatus.Draft)
        {
            return SaveResult.Invalid("id", "Only a draft can be thrown away; a published set stays as history.");
        }

        _db.AttendanceRuleConditions.RemoveRange(draft.Rules.SelectMany(r => r.Conditions));
        _db.AttendanceRules.RemoveRange(draft.Rules);
        _db.AttendanceRuleSets.Remove(draft);
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(draftId);
    }

    private static RuleDto ToDto(RuleSnapshot rule)
    {
        var dto = new RuleDto
        {
            Name = rule.Name,
            IsEnabled = rule.IsEnabled,
            Stage = rule.Stage.ToString(),
            Action = rule.Action.ToString(),
            Finding = rule.Finding?.ToString(),
            Reason = rule.Reason,
            Conditions = rule.Conditions.Select(c => new ConditionDto { Fact = c.Fact.ToString(), Operator = c.Operator.ToString(), Value = c.Value }).ToList(),
        };
        dto.Sentence = RuleSentence.Describe(dto);
        return dto;
    }

    private static RuleSetDto ToDto(AttendanceRuleSet set, string scopeTitle, AttendancePolicy policy) => new()
    {
        Id = set.Id,
        TimeScheduleId = set.TimeScheduleId,
        ScopeTitle = scopeTitle,
        Version = set.Version,
        Status = set.Status.ToString(),
        PublishedAt = set.PublishedAt,
        Notes = set.Notes,
        Rules = RuleSetProvider.ToSnapshot(set, policy).Rules.Select(ToDto).ToList(),
    };

    /// <summary>Makes the set the one in force for its scope as the next version, retires the one before it and audits it. Saves.</summary>
    private async Task PublishSetAsync(AttendanceRuleSet set, IReadOnlyList<RuleDto> rules, string title, string? notes, string? change, CancellationToken cancellationToken)
    {
        var previous = await _db.AttendanceRuleSets.Where(s => s.Status == RuleSetStatus.Published && s.TimeScheduleId == set.TimeScheduleId).ToListAsync(cancellationToken);
        previous.ForEach(p => p.Status = RuleSetStatus.Retired);

        // One sequence for the tenant, so a bigger number is always a later publication.
        var version = (await _db.AttendanceRuleSets.MaxAsync(s => (int?)s.Version, cancellationToken) ?? 0) + 1;
        set.Status = RuleSetStatus.Published;
        set.Version = version;
        set.Title = $"Rules version {version}";
        set.PublishedAt = _clock.GetLocalNow().DateTime;
        set.PublishedById = _user.EmployeeId;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            set.Notes = notes.Trim();
        }

        var suppressed = rules.Where(r => r.IsEnabled && r.Action == nameof(RuleAction.SuppressException)).Select(r => $"{r.Finding}: {r.Reason?.Trim()}").ToList();
        var details = $"{title}: version {version} published, {rules.Count} rule(s), {rules.Count(r => r.IsEnabled)} on."
            + (change is null ? string.Empty : " " + change)
            + (suppressed.Count == 0 ? string.Empty : " Suppresses: " + string.Join("; ", suppressed) + ".");
        _audit.Record(AttendanceAuditActions.RuleSetPublished, nameof(AttendanceRuleSet), set.Id == 0 ? null : set.Id, details);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private void AddRules(AttendanceRuleSet set, IReadOnlyList<RuleDto> rules)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var action = Enum.Parse<RuleAction>(rule.Action);
            var entity = new AttendanceRule
            {
                AttendanceRuleSet = set,
                Priority = (i + 1) * 10,
                Title = rule.Name.Trim(),
                IsEnabled = rule.IsEnabled,
                Stage = Enum.Parse<RuleStage>(rule.Stage),
                Action = action,
                Finding = action is RuleAction.InferBreak or RuleAction.IgnoreShortBreak || RuleSetSnapshot.IsAutoFix(action) || string.IsNullOrWhiteSpace(rule.Finding) ? null : Enum.Parse<RuleFinding>(rule.Finding),
                Reason = action == RuleAction.SuppressException ? rule.Reason?.Trim() : null,
            };
            foreach (var condition in rule.Conditions)
            {
                entity.Conditions.Add(new AttendanceRuleCondition
                {
                    AttendanceRule = entity,
                    Fact = Enum.Parse<RuleFact>(condition.Fact),
                    Operator = Enum.Parse<RuleOperator>(condition.Operator),
                    Value = condition.Value,
                });
            }

            set.Rules.Add(entity);
            _db.AttendanceRules.Add(entity);
        }
    }

    private IQueryable<AttendanceRuleSet> LoadSetsQuery() =>
        _db.AttendanceRuleSets.Include(s => s.Rules).ThenInclude(r => r.Conditions);

    /// <summary>
    /// What a change to the rules in force needs before it is published at once: the scope exists, it has no draft that would be left behind, and the
    /// rules are still the version the person saw. Returns the refusal, or the scope's title and the rules in force.
    /// </summary>
    private async Task<(SaveResult? Refused, string? Title, RuleSetDto? Current)> ReadyToChangeAsync(int? timeScheduleId, int expectedVersion, CancellationToken cancellationToken)
    {
        var title = await ScopeTitleAsync(timeScheduleId, cancellationToken);
        if (title is null)
        {
            return (SaveResult.Invalid(nameof(RuleScopeDto.TimeScheduleId), "Rules can be set for the whole company, or for a schedule that has a policy of its own."), null, null);
        }

        // The change is published at once, so an open draft would be left behind the rules in force.
        if (await _db.AttendanceRuleSets.AnyAsync(s => s.Status == RuleSetStatus.Draft && s.TimeScheduleId == timeScheduleId, cancellationToken))
        {
            return (SaveResult.Invalid("id", "There is a draft of these rules. Continue it or throw it away first."), null, null);
        }

        var current = (await GetCurrentAsync(timeScheduleId, cancellationToken))!;
        return current.Version != expectedVersion
            ? (SaveResult.Invalid("id", "The rules were changed since this page was shown. Look at them again and repeat the change."), null, null)
            : (null, title, current);
    }

    /// <summary>The policy of a scope, whose own numbers start the rules and which a set made before the thresholds moved into the rules still takes its thresholds from; the defaults when there is none.</summary>
    private async Task<AttendancePolicy> PolicyOfAsync(int? timeScheduleId, CancellationToken cancellationToken) =>
        await _db.AttendancePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.TimeScheduleId == timeScheduleId, cancellationToken) ?? new AttendancePolicy();

    /// <summary>The title of a scope, or null when it is not one: a schedule must have a policy of its own for rules to apply to it.</summary>
    private async Task<string?> ScopeTitleAsync(int? timeScheduleId, CancellationToken cancellationToken)
    {
        if (timeScheduleId is null)
        {
            return TenantTitle;
        }

        var hasPolicy = await _db.AttendancePolicies.AnyAsync(p => p.TimeScheduleId == timeScheduleId, cancellationToken);
        return hasPolicy ? await _db.TimeSchedules.AsNoTracking().Where(s => s.Id == timeScheduleId).Select(s => s.Title).FirstOrDefaultAsync(cancellationToken) : null;
    }
}
