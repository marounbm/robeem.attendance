using System.Globalization;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.Contracts.HR.Employees;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendancePolicyService : IAttendancePolicyService
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;

    public AttendancePolicyService(IRobeemDbContext db, IAttendanceAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PolicyListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.AttendancePolicies
            .AsNoTracking()
            .Select(p => new { Policy = p, ScheduleTitle = p.TimeSchedule == null ? null : p.TimeSchedule.Title })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.Policy.TimeScheduleId is null ? 0 : 1)
            .ThenBy(r => r.ScheduleTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Policy.Id)
            .Select(r => new PolicyListItem
            {
                Id = r.Policy.Id,
                Title = r.Policy.Title,
                TimeScheduleId = r.Policy.TimeScheduleId,
                TimeScheduleTitle = r.ScheduleTitle,
                IsTenantDefault = r.Policy.TimeScheduleId is null,
                RoundingRuleMinutes = r.Policy.RoundingRuleMinutes,
                LateGraceMinutes = r.Policy.LateGraceMinutes,
                EarlyDepartureGraceMinutes = r.Policy.EarlyDepartureGraceMinutes,
                OvertimeThresholdMinutes = r.Policy.OvertimeThresholdMinutes,
                RequiresOvertimePreApproval = r.Policy.RequiresOvertimePreApproval,
                CorrectionApprovalWorkflow = r.Policy.CorrectionApprovalWorkflow.ToString(),
                UnlabeledPunchInterpretationMode = r.Policy.UnlabeledPunchInterpretationMode.ToString(),
                DuplicateTimeWindowSeconds = r.Policy.DuplicateTimeWindowSeconds,
                MissingPunchGraceMinutes = r.Policy.MissingPunchGraceMinutes,
            })
            .ToList();
    }

    public async Task<PolicyInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        var policy = await _db.AttendancePolicies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return policy is null
            ? null
            : new PolicyInput
            {
                Title = policy.Title,
                TimeScheduleId = policy.TimeScheduleId,
                RoundingRuleMinutes = policy.RoundingRuleMinutes,
                LateGraceMinutes = policy.LateGraceMinutes,
                EarlyDepartureGraceMinutes = policy.EarlyDepartureGraceMinutes,
                OvertimeThresholdMinutes = policy.OvertimeThresholdMinutes,
                RequiresOvertimePreApproval = policy.RequiresOvertimePreApproval,
                CorrectionApprovalWorkflow = policy.CorrectionApprovalWorkflow.ToString(),
                UnlabeledPunchInterpretationMode = policy.UnlabeledPunchInterpretationMode.ToString(),
                DuplicateTimeWindowSeconds = policy.DuplicateTimeWindowSeconds,
                MissingPunchGraceMinutes = policy.MissingPunchGraceMinutes,
            };
    }

    public async Task<IReadOnlyList<ScheduleOption>> ListScheduleOptionsAsync(CancellationToken cancellationToken)
    {
        var schedules = await _db.TimeSchedules.AsNoTracking().Select(s => new { s.Id, s.Title }).ToListAsync(cancellationToken);
        return schedules.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase).Select(s => new ScheduleOption { Id = s.Id, Title = s.Title }).ToList();
    }

    public async Task<SaveResult> CreateAsync(PolicyInput input, CancellationToken cancellationToken)
    {
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        if (input.TimeScheduleId is { } scheduleId)
        {
            if (!await _db.TimeSchedules.AnyAsync(s => s.Id == scheduleId, cancellationToken))
            {
                return SaveResult.Invalid(nameof(PolicyInput.TimeScheduleId), "That time schedule doesn't exist.");
            }
        }

        if (await _db.AttendancePolicies.AnyAsync(p => p.TimeScheduleId == input.TimeScheduleId, cancellationToken))
        {
            return SaveResult.Invalid(
                nameof(PolicyInput.TimeScheduleId),
                input.TimeScheduleId is null ? "The tenant-wide default policy exists already: change it instead." : "That schedule has a policy already: change it instead.");
        }

        var policy = new AttendancePolicy { TimeScheduleId = input.TimeScheduleId };
        Apply(policy, input);
        _db.AttendancePolicies.Add(policy);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.PolicyChanged, nameof(AttendancePolicy), policy.Id, $"Created '{policy.Title}' for {(input.TimeScheduleId is null ? "the tenant" : $"schedule {input.TimeScheduleId}")}. {Summary(policy)}");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(policy.Id);
    }

    public async Task<SaveResult> UpdateAsync(int id, PolicyInput input, CancellationToken cancellationToken)
    {
        var policy = await _db.AttendancePolicies.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (policy is null)
        {
            return SaveResult.Missing();
        }

        var errors = Validate(input);
        if (input.TimeScheduleId != policy.TimeScheduleId)
        {
            errors[nameof(PolicyInput.TimeScheduleId)] = "A policy can't move to another schedule: delete it and create one for that schedule.";
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = Summary(policy);
        Apply(policy, input);
        var after = Summary(policy);

        _audit.Record(AttendanceAuditActions.PolicyChanged, nameof(AttendancePolicy), policy.Id, $"Changed '{policy.Title}'. Before: {before} After: {after}");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(id);
    }

    public async Task<SaveResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var policy = await _db.AttendancePolicies.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (policy is null)
        {
            return SaveResult.Missing();
        }

        if (policy.IsTenantDefault)
        {
            return SaveResult.Invalid("id", "The tenant-wide default policy can't be deleted: every schedule without its own policy uses it.");
        }

        _db.AttendancePolicies.Remove(policy);
        _audit.Record(AttendanceAuditActions.PolicyChanged, nameof(AttendancePolicy), policy.Id, $"Deleted '{policy.Title}' of schedule {policy.TimeScheduleId}. {Summary(policy)}");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(id);
    }

    private static Dictionary<string, string> Validate(PolicyInput input)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(input.Title))
        {
            errors[nameof(PolicyInput.Title)] = "Give the policy a name.";
        }
        else if (input.Title.Trim().Length > 255)
        {
            errors[nameof(PolicyInput.Title)] = "The name can be at most 255 characters.";
        }

        Range(errors, nameof(PolicyInput.RoundingRuleMinutes), input.RoundingRuleMinutes, 0, PolicyInput.MaxRoundingMinutes, "minutes");
        Range(errors, nameof(PolicyInput.LateGraceMinutes), input.LateGraceMinutes, 0, PolicyInput.MaxGraceMinutes, "minutes");
        Range(errors, nameof(PolicyInput.EarlyDepartureGraceMinutes), input.EarlyDepartureGraceMinutes, 0, PolicyInput.MaxGraceMinutes, "minutes");
        Range(errors, nameof(PolicyInput.OvertimeThresholdMinutes), input.OvertimeThresholdMinutes, 0, PolicyInput.MaxOvertimeThresholdMinutes, "minutes");
        Range(errors, nameof(PolicyInput.DuplicateTimeWindowSeconds), input.DuplicateTimeWindowSeconds, 1, PolicyInput.MaxDuplicateWindowSeconds, "seconds");
        Range(errors, nameof(PolicyInput.MissingPunchGraceMinutes), input.MissingPunchGraceMinutes, 0, PolicyInput.MaxMissingPunchGraceMinutes, "minutes");

        if (!PolicyInput.Workflows.Contains(input.CorrectionApprovalWorkflow, StringComparer.Ordinal))
        {
            errors[nameof(PolicyInput.CorrectionApprovalWorkflow)] = $"Choose one of: {string.Join(", ", PolicyInput.Workflows)}.";
        }

        if (!PolicyInput.InterpretationModes.Contains(input.UnlabeledPunchInterpretationMode, StringComparer.Ordinal))
        {
            errors[nameof(PolicyInput.UnlabeledPunchInterpretationMode)] = $"Choose one of: {string.Join(", ", PolicyInput.InterpretationModes)}.";
        }

        return errors;
    }

    private static void Range(Dictionary<string, string> errors, string field, int value, int min, int max, string unit)
    {
        if (value < min || value > max)
        {
            errors[field] = $"Use between {min} and {max} {unit}.";
        }
    }

    private static void Apply(AttendancePolicy policy, PolicyInput input)
    {
        policy.Title = input.Title.Trim();
        policy.RoundingRuleMinutes = input.RoundingRuleMinutes;
        policy.LateGraceMinutes = input.LateGraceMinutes;
        policy.EarlyDepartureGraceMinutes = input.EarlyDepartureGraceMinutes;
        policy.OvertimeThresholdMinutes = input.OvertimeThresholdMinutes;
        policy.RequiresOvertimePreApproval = input.RequiresOvertimePreApproval;
        policy.CorrectionApprovalWorkflow = Enum.Parse<CorrectionApprovalWorkflow>(input.CorrectionApprovalWorkflow);
        policy.UnlabeledPunchInterpretationMode = Enum.Parse<UnlabeledPunchInterpretationMode>(input.UnlabeledPunchInterpretationMode);
        policy.DuplicateTimeWindowSeconds = input.DuplicateTimeWindowSeconds;
        policy.MissingPunchGraceMinutes = input.MissingPunchGraceMinutes;
    }

    private static string Summary(AttendancePolicy p) => string.Create(
        CultureInfo.InvariantCulture,
        $"rounding {p.RoundingRuleMinutes} min, late grace {p.LateGraceMinutes} min, early grace {p.EarlyDepartureGraceMinutes} min, overtime after {p.OvertimeThresholdMinutes} min, overtime pre-approval {(p.RequiresOvertimePreApproval ? "on" : "off")}, corrections by {p.CorrectionApprovalWorkflow}, unlabeled punches {p.UnlabeledPunchInterpretationMode}, duplicate window {p.DuplicateTimeWindowSeconds} s, missing-punch grace {p.MissingPunchGraceMinutes} min.");
}
