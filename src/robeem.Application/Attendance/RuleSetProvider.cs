using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class RuleSetProvider : IRuleSetProvider
{
    private readonly IRobeemDbContext _db;

    public RuleSetProvider(IRobeemDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Reads a stored rule set (with its rules and conditions loaded) as the evaluator reads it. A set made before the thresholds moved into the
    /// rules (<see cref="AttendanceRuleSet.ThresholdsInRules"/> false) is read with the policy's thresholds added, so it behaves as it always did.
    /// </summary>
    public static RuleSetSnapshot ToSnapshot(AttendanceRuleSet set, AttendancePolicy policy)
    {
        var snapshot = new RuleSetSnapshot(
            set.Version,
            $"published version {set.Version}",
            set.Rules
                .OrderBy(r => r.Priority)
                .ThenBy(r => r.Id)
                .Select(r => new RuleSnapshot(
                    r.Title,
                    r.Priority,
                    r.IsEnabled,
                    r.Stage,
                    r.Action,
                    r.Finding,
                    r.Reason,
                    r.Conditions.OrderBy(c => c.Id).Select(c => new ConditionSnapshot(c.Fact, c.Operator, c.Value)).ToList()))
                .ToList());
        snapshot = set.ThresholdsInRules ? snapshot : snapshot.WithPolicyThresholds(policy);

        // A set made before a day worked short was a finding has no rule for it: it is read with the policy's own, on.
        return set.ShortHoursInRules ? snapshot : snapshot.WithShortHours(policy);
    }

    public async Task<RuleSetSnapshot> ResolveAsync(AttendancePolicy policy, CancellationToken cancellationToken)
    {
        var scope = policy.TimeScheduleId;
        var set = await _db.AttendanceRuleSets
            .AsNoTracking()
            .Include(s => s.Rules).ThenInclude(r => r.Conditions)
            .Where(s => s.Status == RuleSetStatus.Published && s.TimeScheduleId == scope)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(cancellationToken);
        return set is null ? RuleSetSnapshot.FromPolicy(policy) : ToSnapshot(set, policy);
    }

    public async Task<IReadOnlyDictionary<int, int>> GetPublishedVersionsAsync(CancellationToken cancellationToken)
    {
        var sets = await _db.AttendanceRuleSets
            .AsNoTracking()
            .Where(s => s.Status == RuleSetStatus.Published)
            .Select(s => new { s.TimeScheduleId, s.Version })
            .ToListAsync(cancellationToken);
        return sets.GroupBy(s => s.TimeScheduleId ?? IRuleSetProvider.TenantScope).ToDictionary(g => g.Key, g => g.Max(s => s.Version));
    }
}
