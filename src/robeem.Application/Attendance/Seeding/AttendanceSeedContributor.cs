using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Application.Seeding;
using robeem.DataModel;

namespace robeem.Application.Attendance.Seeding;

/// <summary>
/// Gives a tenant database the tenant-wide default attendance policy (study §20.1). Adds only what is missing, so an
/// edit made by an administrator is never overwritten. The attendance catalog (transports, device types, payload formats, vendors and
/// connectors) is not a tenant's: it belongs to the prime tenant and is seeded by <see cref="CatalogSeedContributor"/>.
/// </summary>
public sealed class AttendanceSeedContributor : ISeedContributor
{
    /// <summary>Gets the run order; after the HR seed.</summary>
    public int Order => 200;

    public async Task SeedAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        // The MVP defaults of study §20.1: no inference of unlabeled punches, no overtime
        // pre-approval, the manager approves corrections. An administrator changes them in place.
        if (!await db.AttendancePolicies.AnyAsync(p => p.TimeScheduleId == null, cancellationToken))
        {
            db.AttendancePolicies.Add(new AttendancePolicy
            {
                Title = "Default attendance policy",
                UnlabeledPunchInterpretationMode = UnlabeledPunchInterpretationMode.Off,
                RequiresOvertimePreApproval = false,
                CorrectionApprovalWorkflow = CorrectionApprovalWorkflow.Manager,
            });
        }
    }
}
