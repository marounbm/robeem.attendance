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

    /// <summary>Reads a stored rule set (with its rules and conditions loaded) as the evaluator reads it.</summary>
    public static RuleSetSnapshot ToSnapshot(AttendanceRuleSet set) => new(
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

    public async Task<RuleSetSnapshot> ResolveAsync(AttendancePolicy policy, CancellationToken cancellationToken)
    {
        var scope = policy.TimeScheduleId;
        var set = await _db.AttendanceRuleSets
            .AsNoTracking()
            .Include(s => s.Rules).ThenInclude(r => r.Conditions)
            .Where(s => s.Status == RuleSetStatus.Published && s.TimeScheduleId == scope)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(cancellationToken);
        return set is null ? RuleSetSnapshot.FromPolicy(policy) : ToSnapshot(set);
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
