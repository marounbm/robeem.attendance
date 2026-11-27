using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Application.Attendance.TestEmployees;
using robeem.Application.Security;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;
using robeem.Tests.Attendance.Scenarios;

namespace robeem.Tests.Attendance;

/// <summary>
/// Deleting the employees of a CSV file with everything of theirs. Two employees (a schedule with a break, and one without) have their files uploaded,
/// normalized and calculated; one is deleted, and the other has to be left exactly as it was.
/// </summary>
public sealed class TestEmployeeRemoverTests : IDisposable
{
    private readonly ScenarioHarness _harness = new(ScenarioGroup.All.Where(g => g.Prefix is "SW" or "FDW").ToList(), coverage: true);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task APreview_SaysWhatWouldGo_AndDeletesNothing()
    {
        await PrepareAsync();
        var before = Snapshot();

        var outcome = await RemoveAsync(File("100001"), preview: true);

        Assert.Equal(TestEmployeeDeleteStatus.Done, outcome.Status);
        var result = outcome.Result!;
        Assert.False(result.Deleted);
        Assert.Equal(1, result.Employees);
        Assert.True(Count(result, "Raw transactions") > 0 && Count(result, "Normalized transactions") > 0 && Count(result, "Attendance records") > 0);
        Assert.Equal(1, Count(result, "Employees"));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Deleting_RemovesTheEmployeeAndEverythingOfTheirs_AndLeavesTheOtherEmployeeUntouched()
    {
        await PrepareAsync();
        var other = Snapshot("100004");
        AddPayRowsFor("100001", "100004");
        var previewed = (await RemoveAsync(File("100001"), preview: true)).Result!;

        var outcome = await RemoveAsync(File("100001", "NO-SUCH"), confirmation: TestDatabase.TenantA);

        Assert.Equal(TestEmployeeDeleteStatus.Done, outcome.Status);
        var result = outcome.Result!;
        Assert.True(result.Deleted);
        Assert.Equal(["NO-SUCH"], result.NotFound);

        // What the preview promised is what went.
        Assert.Equal(previewed.Counts.Select(c => (c.Name, c.Count)), result.Counts.Select(c => (c.Name, c.Count)));

        // Nothing of the deleted employee is left, however it is found: not the employee, the calculated days, the punches (the ones that failed too), the file.
        using (var db = _harness.Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.DoesNotContain(db.Profiles, p => p.EmployeeNumber == "100001");
            Assert.DoesNotContain(db.RawAttendanceTransactions, r => r.ExternalEmployeeId == "100001");
            Assert.Empty(db.AttendanceUploadedFiles.Where(f => f.FileName.StartsWith("100001")));
            Assert.DoesNotContain(db.ExternalEmployeeIdMappings, m => m.ExternalEmployeeId == "100001");
            var entry = Assert.Single(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.TestEmployeesDeleted);
            Assert.Contains("100001", entry.Details);

            // The pay data of the deleted employee goes with them: decisions, their own pay cycle and their closed period. The other's stays.
            var remaining = db.Profiles.Single(p => p.EmployeeNumber == "100004").Id;
            Assert.Equal(remaining, Assert.Single(db.PayCompensationDecisions).EmployeeId);
            Assert.Equal(remaining, Assert.Single(db.PayCycles).EmployeeId);
            Assert.Equal(remaining, Assert.Single(db.PayPeriods).EmployeeId);
            Assert.Equal(1, Count(result, "Compensation decisions"));
        }

        // The other employee has all of theirs, as it was.
        Assert.Equal(other, Snapshot("100004"));
        Assert.True(other.Raw > 0 && other.Records > 0);
    }

    [Fact]
    public async Task WithoutTheTenantIdTyped_OrWhereTheHostHasNotAllowedIt_NothingIsDeleted()
    {
        await PrepareAsync();
        var before = Snapshot();

        Assert.Equal(TestEmployeeDeleteStatus.ConfirmationMismatch, (await RemoveAsync(File("100001"), confirmation: "tenant")).Status);
        Assert.Equal(TestEmployeeDeleteStatus.ConfirmationMismatch, (await RemoveAsync(File("100001"), confirmation: string.Empty)).Status);
        Assert.Equal(TestEmployeeDeleteStatus.Disabled, (await RemoveAsync(File("100001"), confirmation: TestDatabase.TenantA, enabled: false)).Status);
        Assert.Equal(TestEmployeeDeleteStatus.Disabled, (await RemoveAsync(File("100001"), preview: true, enabled: false)).Status);

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task AFileWithNoKnownEmployee_OrNoNumberColumn_IsRefused()
    {
        await PrepareAsync();

        var unknown = await RemoveAsync(File("NOBODY"), confirmation: TestDatabase.TenantA);
        var noColumn = await RemoveAsync("name\r\nAda\r\n", confirmation: TestDatabase.TenantA);
        var empty = await RemoveAsync(string.Empty, confirmation: TestDatabase.TenantA);

        Assert.All(new[] { unknown, noColumn, empty }, o => Assert.Equal(TestEmployeeDeleteStatus.Invalid, o.Status));
        Assert.Contains("nothing to delete", unknown.Errors.Single());
        Assert.Contains("employee_number", noColumn.Errors.Single());
        using var db = _harness.Database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(db.Profiles, p => p.EmployeeNumber == "100001");
    }

    [Fact]
    public async Task AnEmployeeWhoIsSomeonesManager_IsNotDeleted_UntilThatSomeoneIsInTheFileToo()
    {
        await PrepareAsync();
        using (var db = _harness.Database.CreateContext(TestDatabase.TenantA))
        {
            db.Profiles.Single(p => p.EmployeeNumber == "100004").ManagerId = db.Profiles.Single(p => p.EmployeeNumber == "100001").Id;
            db.SaveChanges();
        }

        var blocked = await RemoveAsync(File("100001"), confirmation: TestDatabase.TenantA);
        var both = await RemoveAsync(File("100001", "100004"), confirmation: TestDatabase.TenantA);

        Assert.Equal(TestEmployeeDeleteStatus.Blocked, blocked.Status);
        Assert.Contains("100004", blocked.Errors.Single());
        Assert.Equal(TestEmployeeDeleteStatus.Done, both.Status);
        Assert.Equal(2, both.Result!.Employees);
        using var check = _harness.Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.Profiles);
        Assert.Empty(check.RawAttendanceTransactions.Where(r => r.ExternalTransactionId != null));
    }

    private static string File(params string[] numbers) => "employee_number\r\n" + string.Join("\r\n", numbers) + "\r\n";

    private static int Count(robeem.Contracts.Attendance.TestEmployeeDeleteResult result, string name) => result.Counts.Single(c => c.Name == name).Count;

    /// <summary>Gives each employee a compensation decision, a pay cycle of their own and a closed pay period.</summary>
    private void AddPayRowsFor(params string[] numbers)
    {
        using var db = _harness.Database.CreateContext(TestDatabase.TenantA);
        foreach (var id in numbers.Select(n => db.Profiles.Single(p => p.EmployeeNumber == n).Id))
        {
            db.PayCompensationDecisions.Add(new PayCompensationDecision
            {
                EmployeeId = id,
                AttendanceDate = new DateTime(2026, 4, 6),
                LineKind = CompensationLineKind.Overtime,
                Decision = CompensationDecisionKind.Ignore,
                Minutes = 30,
                DecidedById = id,
                DecidedAt = new DateTime(2026, 5, 1),
            });
            db.PayCycles.Add(new PayCycle { EmployeeId = id, Frequency = PayCycleFrequency.Weekly });
            db.PayPeriods.Add(new PayPeriod { EmployeeId = id, StartDate = new DateTime(2026, 4, 1), EndDate = new DateTime(2026, 4, 30), IsClosed = true });
        }

        db.SaveChanges();
    }

    private async Task<TestEmployeeDeleteOutcome> RemoveAsync(string csv, string? confirmation = null, bool preview = false, bool enabled = true)
    {
        using var db = _harness.Database.CreateContext(TestDatabase.TenantA);
        var remover = new TestEmployeeRemover(db, new AttendanceAuditTrail(db, new SystemCurrentUser(), TimeProvider.System), Options.Create(new TestEmployeeOptions { Enabled = enabled }));
        using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(csv));
        return preview ? await remover.PreviewAsync(stream, CancellationToken.None) : await remover.DeleteAsync(stream, confirmation ?? string.Empty, CancellationToken.None);
    }

    /// <summary>Uploads, normalizes and calculates two weeks of the files of the two employees, as the real flow does.</summary>
    private async Task PrepareAsync()
    {
        var adapters = new ServiceCollection()
            .AddSingleton<ICustomFormatFileReader, CustomFormatFileReader>()
            .AddSingleton<ICustomFormatInterpreterFactory, CustomFormatInterpreterFactory>()
            .BuildServiceProvider();
        var first = new DateTime(2026, 4, 1);
        var days = _harness.People
            .SelectMany(p => ScenarioPlanner.PlanAll(p.Group, p.Case, first, first.AddDays(13)).Select(plan => (Person: p, Plan: plan)))
            .Where(d => d.Plan.Shift is not null || d.Plan.Punches.Count > 0)
            .ToList();

        foreach (var person in _harness.People)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{person.Number}.csv");
            try
            {
                await global::System.IO.File.WriteAllTextAsync(path, ScenarioCsv.Build(person.Number, days.Where(d => d.Person == person).Select(d => d.Plan)));
                using var db = _harness.Database.CreateContext(TestDatabase.TenantA);
                var service = new IngestionService(db, _harness.Database.CreateCatalogReader(), new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), adapters);
                Assert.NotNull(await service.IngestUploadedFileAsync(_harness.IntegrationId, path, $"{person.Number}.csv", null, CancellationToken.None));
            }
            finally
            {
                global::System.IO.File.Delete(path);
            }
        }

        using (var db = _harness.Database.CreateContext(TestDatabase.TenantA))
        {
            var normalization = new NormalizationService(db, _harness.Database.CreateCatalogReader(), new NormalizedTransactionRecorder(db), new AttendancePolicyResolver(db), adapters, NullLogger<NormalizationService>.Instance);
            foreach (var batchId in await normalization.ListBatchesWithPendingTransactionsAsync(CancellationToken.None))
            {
                await normalization.NormalizeBatchAsync(batchId, CancellationToken.None);
            }
        }

        await _harness.CalculateAsync(days);
    }

    /// <summary>The rows there are, for everyone or for one employee: found the way the deletion finds them.</summary>
    private Rows Snapshot(string? employeeNumber = null)
    {
        using var db = _harness.Database.CreateContext(TestDatabase.TenantA);
        var employees = db.Profiles.Where(p => employeeNumber == null || p.EmployeeNumber == employeeNumber).Select(p => p.Id).ToList();
        return new Rows(
            db.RawAttendanceTransactions.Count(r => r.ExternalTransactionId != null && (employeeNumber == null || r.ExternalEmployeeId == employeeNumber)),
            db.NormalizedAttendanceTransactions.Count(n => n.EmployeeId != null && employees.Contains(n.EmployeeId.Value)),
            db.AttendanceRecords.Count(r => employees.Contains(r.EmployeeId)),
            db.AttendanceDaySummaries.Count(d => employees.Contains(d.EmployeeId)),
            db.AttendanceExceptions.Count(e => employees.Contains(e.EmployeeId)),
            db.AttendanceUploadedFiles.Count(f => employeeNumber == null || f.FileName.StartsWith(employeeNumber)),
            db.Profiles.Count(p => employeeNumber == null || p.EmployeeNumber == employeeNumber));
    }

    private sealed record Rows(int Raw, int Normalized, int Records, int Summaries, int Exceptions, int Files, int Employees);
}
