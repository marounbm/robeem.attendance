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
            return ToDto(published, title);
        }

        // Nothing is published: the rules are the policy's own settings.
        var policy = await _db.AttendancePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.TimeScheduleId == timeScheduleId, cancellationToken) ?? new AttendancePolicy();
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
        return draft is null ? null : ToDto(draft, title!);
    }

    public async Task<RuleSetDto?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var set = await LoadSetsQuery().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return set is null ? null : ToDto(set, await ScopeTitleAsync(set.TimeScheduleId, cancellationToken) ?? TenantTitle);
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
        var draft = new AttendanceRuleSet { TimeScheduleId = timeScheduleId, Status = RuleSetStatus.Draft, Version = 0, Title = "Draft rules" };
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

        var title = await ScopeTitleAsync(draft.TimeScheduleId, cancellationToken) ?? TenantTitle;
        var dto = ToDto(draft, title);
        var errors = RuleValidator.Validate(dto.Rules);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var previous = await _db.AttendanceRuleSets.Where(s => s.Status == RuleSetStatus.Published && s.TimeScheduleId == draft.TimeScheduleId).ToListAsync(cancellationToken);
        previous.ForEach(p => p.Status = RuleSetStatus.Retired);

        // One sequence for the tenant, so a bigger number is always a later publication.
        var version = (await _db.AttendanceRuleSets.MaxAsync(s => (int?)s.Version, cancellationToken) ?? 0) + 1;
        draft.Status = RuleSetStatus.Published;
        draft.Version = version;
        draft.Title = $"Rules version {version}";
        draft.PublishedAt = _clock.GetLocalNow().DateTime;
        draft.PublishedById = _user.EmployeeId;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            draft.Notes = notes.Trim();
        }

        var suppressed = dto.Rules.Where(r => r.IsEnabled && r.Action == nameof(RuleAction.SuppressException)).Select(r => $"{r.Finding}: {r.Reason?.Trim()}").ToList();
        var details = $"{title}: version {version} published, {dto.Rules.Count} rule(s), {dto.Rules.Count(r => r.IsEnabled)} on."
            + (suppressed.Count == 0 ? string.Empty : " Suppresses: " + string.Join("; ", suppressed) + ".");
        _audit.Record(AttendanceAuditActions.RuleSetPublished, nameof(AttendanceRuleSet), draft.Id, details);
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(draft.Id);
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

    private static RuleSetDto ToDto(AttendanceRuleSet set, string scopeTitle) => new()
    {
        Id = set.Id,
        TimeScheduleId = set.TimeScheduleId,
        ScopeTitle = scopeTitle,
        Version = set.Version,
        Status = set.Status.ToString(),
        PublishedAt = set.PublishedAt,
        Notes = set.Notes,
        Rules = RuleSetProvider.ToSnapshot(set).Rules.Select(ToDto).ToList(),
    };

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
                Finding = action == RuleAction.InferBreak || string.IsNullOrWhiteSpace(rule.Finding) ? null : Enum.Parse<RuleFinding>(rule.Finding),
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
