using robeem.Application.Attendance.Jobs;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>The history of the background job runs: written as Running, settled when they end, listed newest first, pruned by age.</summary>
public sealed class JobRunLogTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 12, 2, 0, 0);

    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ARun_IsWrittenAsRunning_ThenSettledWithItsOutcome()
    {
        int id;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            id = await new JobRunLog(db).BeginAsync("nightly", JobTrigger.Scheduled, JobRunStatus.Running, Start, Start, null, null, null, CancellationToken.None);
        }

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var run = Assert.Single(await new JobRunLog(db).ListAsync(null, null, 10, CancellationToken.None));
            Assert.Equal(("nightly", "Scheduled", "Running", null), (run.JobName, run.Trigger, run.Status, run.CompletedAt));
            await new JobRunLog(db).CompleteAsync(id, JobRunStatus.Succeeded, 7, "7 things", Start.AddSeconds(5), CancellationToken.None);
        }

        using var read = _database.CreateContext(TestDatabase.TenantA);
        var settled = Assert.Single(await new JobRunLog(read).ListAsync(null, null, 10, CancellationToken.None));
        Assert.Equal(("Succeeded", 7, "7 things", (DateTime?)Start.AddSeconds(5)), (settled.Status, settled.WorkItems, settled.Summary, settled.CompletedAt));
    }

    [Fact]
    public async Task TheHistory_IsNewestFirst_AndFiltersByJobAndStatus()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var log = new JobRunLog(db);
        await log.BeginAsync("a", JobTrigger.Scheduled, JobRunStatus.Succeeded, null, Start, Start, null, null, CancellationToken.None);
        await log.BeginAsync("b", JobTrigger.Manual, JobRunStatus.Failed, null, Start, Start, "boom", null, CancellationToken.None);
        await log.BeginAsync("a", JobTrigger.Scheduled, JobRunStatus.Failed, null, Start, Start, "boom", null, CancellationToken.None);

        Assert.Equal(["a", "b", "a"], (await log.ListAsync(null, null, 10, CancellationToken.None)).Select(r => r.JobName));
        Assert.Equal(["a", "a"], (await log.ListAsync("a", null, 10, CancellationToken.None)).Select(r => r.JobName));
        Assert.Equal(["a", "b"], (await log.ListAsync(null, "failed", 10, CancellationToken.None)).Select(r => r.JobName));
        Assert.Single(await log.ListAsync(null, null, 1, CancellationToken.None));

        // The latest stored run of each job.
        var last = await log.LastRunsAsync(CancellationToken.None);
        Assert.Equal(("Failed", "Failed"), (last["a"].Status, last["b"].Status));
    }

    [Fact]
    public async Task AManualRun_NamesWhoStartedIt_AndASummaryTooLongIsCut()
    {
        int employeeId;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var employee = new Profile { EmployeeNumber = "I-1", FirstName = "Ines", LastName = "Park", Title = "Ines Park" };
            db.Profiles.Add(employee);
            db.SaveChanges();
            employeeId = employee.Id;
        }

        using var read = _database.CreateContext(TestDatabase.TenantA);
        var log = new JobRunLog(read);
        await log.BeginAsync("a", JobTrigger.Manual, JobRunStatus.Failed, null, Start, Start, new string('x', 5000), employeeId, CancellationToken.None);
        await log.BeginAsync("a", JobTrigger.Manual, JobRunStatus.Succeeded, null, Start, Start, null, 99999, CancellationToken.None);

        var runs = await log.ListAsync(null, null, 10, CancellationToken.None);

        // A token whose employee id is no profile of the tenant is recorded without a name rather than refused.
        Assert.Equal(string.Empty, runs[0].RequestedByName);
        Assert.Equal(("Ines Park", AttendanceJobRun.SummaryMaxLength), (runs[1].RequestedByName, runs[1].Summary!.Length));
    }

    [Fact]
    public async Task LastScheduledAt_IgnoresManualRuns_AndCountsMissedOnes()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var log = new JobRunLog(db);
        Assert.Null(await log.LastScheduledAtAsync("a", CancellationToken.None));

        await log.BeginAsync("a", JobTrigger.Scheduled, JobRunStatus.Succeeded, Start, Start.AddSeconds(1), Start.AddSeconds(2), null, null, CancellationToken.None);
        await log.BeginAsync("a", JobTrigger.Manual, JobRunStatus.Succeeded, null, Start.AddDays(1), Start.AddDays(1), null, null, CancellationToken.None);
        Assert.Equal(Start, await log.LastScheduledAtAsync("a", CancellationToken.None));

        await log.BeginAsync("a", JobTrigger.Scheduled, JobRunStatus.Missed, Start.AddDays(2), Start.AddDays(3), Start.AddDays(3), null, null, CancellationToken.None);
        Assert.Equal(Start.AddDays(2), await log.LastScheduledAtAsync("a", CancellationToken.None));
        Assert.Null(await log.LastScheduledAtAsync("other", CancellationToken.None));
    }

    [Fact]
    public async Task RunsLeftRunning_AreSettledAsInterrupted_AndOldOnesPruned()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var log = new JobRunLog(db);
        await log.BeginAsync("a", JobTrigger.Scheduled, JobRunStatus.Running, Start, Start, null, null, null, CancellationToken.None);
        await log.BeginAsync("a", JobTrigger.Scheduled, JobRunStatus.Succeeded, null, Start.AddDays(-40), Start.AddDays(-40), null, null, CancellationToken.None);
        await log.BeginAsync("a", JobTrigger.Scheduled, JobRunStatus.Succeeded, null, Start.AddDays(-1), Start.AddDays(-1), null, null, CancellationToken.None);

        Assert.Equal(1, await log.FailInterruptedAsync(Start.AddHours(1), CancellationToken.None));
        var interrupted = (await log.ListAsync(null, "Failed", 10, CancellationToken.None)).Single();
        Assert.Contains("Interrupted", interrupted.Summary);

        Assert.Equal(1, await log.PruneAsync(Start.AddDays(-30), CancellationToken.None));
        Assert.Equal(2, (await log.ListAsync(null, null, 10, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task OneTenantsHistory_IsInvisibleToAnother()
    {
        using (var a = _database.CreateContext(TestDatabase.TenantA))
        {
            await new JobRunLog(a).BeginAsync("a", JobTrigger.Scheduled, JobRunStatus.Succeeded, null, Start, Start, null, null, CancellationToken.None);
        }

        using var b = _database.CreateContext(TestDatabase.TenantB);
        Assert.Empty(await new JobRunLog(b).ListAsync(null, null, 10, CancellationToken.None));
    }
}
