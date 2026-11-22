using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Application.Attendance.Seeding;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using Xunit.Abstractions;

namespace robeem.Tests.Attendance.Scenarios;

/// <summary>
/// Every kind of time schedule against every kind of day, under many combinations of the rules. Each employee of the scenario data is one
/// case on one schedule; its punches are made by <see cref="ScenarioPlanner"/>, the answer for each day is worked out by
/// <see cref="ScenarioOracle"/> without the engine, and the engine's answer must be the same.
/// </summary>
public sealed class ScheduleScenarioTests
{
    private static readonly DateTime MatrixFirst = new(2026, 4, 1);

    private static readonly DateTime MatrixLast = new(2026, 4, 6);

    /// <summary>One schedule of each shape the rules meet: a shift with a break, one without, an overnight one, and a long one; plus hourly staff.</summary>
    private static readonly string[] MatrixGroups = ["SW", "CO7D-MT", "CO7D-NT", "FDW", "ODHS"];

    private readonly ITestOutputHelper _output;

    public ScheduleScenarioTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> ProfileNames() => RuleProfile.All.Select(p => new object[] { p.Name });

    [Fact]
    public void TheEmployees_AreNamedAfterTheirSchedule_AndEveryNumberIsUnique()
    {
        var people = ScenarioGroup.All.SelectMany(g => g.Cases.Select(c => g.EmployeeFor(c))).ToList();

        Assert.Equal(people.Count, people.Select(p => p.Number).Distinct(StringComparer.Ordinal).Count());
        Assert.All(people, p => Assert.True(p.Number.Length <= 50, p.Number));
        Assert.Contains(people, p => p is { Number: "SW-LATE", FirstName: "Standard Week", LastName: "Arrives late" });
        Assert.Contains(people, p => p is { Number: "CO7D-NT-OVERTIME", FirstName: "Continuous Operations (7-Day) / Night Team" });
        Assert.Contains(people, p => p is { Number: "ODHS-H_PERFECT", ScheduleTitle: "On Demand (Hourly Staff)" });

        // Every case of the catalog belongs to some schedule, so none is left untested.
        Assert.All(AttendanceScenarioCatalog.Cases, c => Assert.Contains(people, p => p.Case == c));

        // The employees of the files are named the same way, one (or a few) for each schedule and team that works.
        var files = ScenarioGroup.All.SelectMany(g => g.CoverageCases.Select(c => g.EmployeeFor(c).Number)).ToList();
        Assert.Equal(files.Count, files.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("SW-ALL", files);
        Assert.Contains("AWS-WA-ALL3", files);
        Assert.Contains("ODHS-ALL", files);
        Assert.Equal(17, files.Count);
    }

    [Fact]
    public void EveryFile_HoldsEveryKindOfDayItsScheduleHas_AcrossTheWholePeriod()
    {
        var missing = new List<string>();
        foreach (var group in ScenarioGroup.All)
        {
            var plans = group.CoverageCases.SelectMany(c => ScenarioPlanner.PlanAll(group, c, ScenarioGroup.FirstDay, ScenarioGroup.LastDay).Select(p => (Case: c, Plan: p))).ToList();
            var expected = ScenarioPlanner.CoverageKinds(group);
            missing.AddRange(expected.Where(kind => plans.All(p => p.Plan.Kind != kind)).Select(kind => $"{group.Prefix}: no day of {kind}"));

            // Each file spans the period, and is all the group's days: a day that is worked has a kind of day.
            foreach (var file in plans.GroupBy(p => p.Case))
            {
                var days = file.Select(p => p.Plan).ToList();
                var number = group.EmployeeFor(file.Key).Number;
                if (days.Count != 60 || days[0].Date != ScenarioGroup.FirstDay || days[^1].Date != ScenarioGroup.LastDay)
                {
                    missing.Add($"{number}: its days are not the whole period");
                }

                if (days.Where(d => d.Punches.Count > 0).Select(d => d.Date.Month).Distinct().Count() < 2)
                {
                    missing.Add($"{number}: has punches in one month only");
                }

                // Every kind that is this employee's comes round at least once.
                var mine = expected.Where((_, i) => i % group.CoverageCases.Count == group.CoverageCases.ToList().FindIndex(c => c == file.Key)).ToList();
                missing.AddRange(mine.Where(kind => days.All(d => d.Kind != kind)).Select(kind => $"{number}: no day of {kind}"));
            }
        }

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public async Task EverySeededSchedule_ResolvesEveryDayOfThePeriod_ToTheShiftItIsMeantToWork()
    {
        using var harness = new ScenarioHarness(ScenarioGroup.All);
        var wrong = new List<string>();
        foreach (var person in harness.People.Where(p => p.Case.Code is "PERFECT" or "OFF_DAY" or "H_PERFECT"))
        {
            using var db = harness.Database.CreateContext(TestDatabase.TenantA);
            var days = await new ScheduleResolutionService(db).ResolveShiftsInRangeAsync(person.EmployeeId, ScenarioGroup.FirstDay, ScenarioGroup.LastDay, CancellationToken.None);
            foreach (var day in days)
            {
                var expected = person.Group.ShiftOn(day.Date);
                var actual = day.Shifts.Count == 1 ? day.Shifts[0] : null;
                var sameShift = expected is null
                    ? day.Shifts.Count == 0
                    : actual is not null && actual.StartsAt == expected.StartsOn(day.Date) && actual.EndsAt == expected.EndsOn(day.Date) && actual.BreakTime == expected.HasBreak;
                if (!sameShift)
                {
                    wrong.Add($"{person.Number} {day.Date:yyyy-MM-dd}: expected {(expected is null ? "no shift" : $"{expected.Start}-{expected.End}")}, got {string.Join(", ", day.Shifts.Select(s => $"{s.StartTime}-{s.EndTime}"))} ({day.Outcome})");
                }
            }
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count} day(s) resolve to the wrong shift:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(30))}");
    }

    [Theory]
    [MemberData(nameof(ProfileNames))]
    public async Task EveryCase_OnEverySortOfSchedule_ComesOutAsTheRulesSay(string profileName)
    {
        var profile = RuleProfile.All.Single(p => p.Name == profileName);
        using var harness = new ScenarioHarness(ScenarioGroup.All.Where(g => MatrixGroups.Contains(g.Prefix)).ToList());
        var settings = await harness.ApplyAsync(profile);

        var days = Plans(harness, MatrixFirst, MatrixLast);
        harness.StorePunches(days, settings.Sequential);
        await harness.CalculateAsync(days);

        AssertNoMismatches(Mismatches(harness, days, settings));
    }

    /// <summary>
    /// The file of every employee, for the whole period: uploaded, normalized and calculated, and every day compared with what the starting
    /// rules say it must come to.
    /// </summary>
    [Fact]
    public async Task TheFileOfEveryEmployee_GoesThroughUploadNormalizationAndTheEngine_ForTheWholePeriod_AsTheStartingRulesSay()
    {
        var groups = ScenarioGroup.All;
        var (first, last) = (ScenarioGroup.FirstDay, ScenarioGroup.LastDay);
        var clock = global::System.Diagnostics.Stopwatch.StartNew();
        using var harness = new ScenarioHarness(groups, coverage: true);
        var settings = await harness.ApplyAsync(RuleProfile.All[0]);
        var days = Plans(harness, first, last);
        _output.WriteLine($"{clock.Elapsed}: {harness.People.Count} employees, {days.Count} days made");
        var adapters = new ServiceCollection()
            .AddSingleton<ICustomFormatFileReader, CustomFormatFileReader>()
            .AddSingleton<ICustomFormatInterpreterFactory, CustomFormatInterpreterFactory>()
            .BuildServiceProvider();

        // One file per employee, uploaded as a person would.
        foreach (var person in harness.People)
        {
            var path = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(path, ScenarioCsv.Build(person.Number, days.Where(d => d.Person == person).Select(d => d.Plan)));
                using var db = harness.Database.CreateContext(TestDatabase.TenantA);
                var service = new IngestionService(db, harness.Database.CreateCatalogReader(), new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), adapters);
                Assert.NotNull(await service.IngestManualUploadAsync(harness.IntegrationId, path, CancellationToken.None));
            }
            finally
            {
                File.Delete(path);
            }
        }

        _output.WriteLine($"{clock.Elapsed}: files uploaded");
        using (var db = harness.Database.CreateContext(TestDatabase.TenantA))
        {
            var normalization = new NormalizationService(db, harness.Database.CreateCatalogReader(), new NormalizedTransactionRecorder(db), new AttendancePolicyResolver(db), adapters, NullLogger<NormalizationService>.Instance);
            foreach (var batchId in await normalization.ListBatchesWithPendingTransactionsAsync(CancellationToken.None))
            {
                await normalization.NormalizeBatchAsync(batchId, CancellationToken.None);
            }
        }

        _output.WriteLine($"{clock.Elapsed}: normalized");
        using (var db = harness.Database.CreateContext(TestDatabase.TenantA))
        {
            // With the starting rules a punch with no type is refused (its raw row is kept, marked failed); every other punch is a normalized one.
            var untyped = days.Sum(d => d.Plan.Punches.Count(p => p.Type == NormalizedEventType.Unknown));
            var failed = db.RawAttendanceTransactions.Where(r => r.ExternalTransactionId != null && r.ProcessingStatus == RawTransactionProcessingStatus.Failed).Select(r => r.ProcessingError).ToList();
            var others = db.RawAttendanceTransactions
                .Where(r => r.ExternalTransactionId != null && r.ProcessingStatus == RawTransactionProcessingStatus.Failed && !r.ProcessingError!.Contains("no event type or direction"))
                .Select(r => r.ExternalTransactionId + " " + r.TransactionTimestampRaw + " " + r.ProcessingError)
                .Take(5)
                .ToList();
            Assert.True(others.Count == 0, $"Other failures: {string.Join(" | ", others)}");
            Assert.Equal(untyped, failed.Count);
            Assert.All(failed, error => Assert.Contains("no event type or direction", error));
            Assert.Equal(days.Sum(d => d.Plan.Punches.Count) - untyped, db.NormalizedAttendanceTransactions.Count(n => n.DuplicateStatus == DuplicateStatus.Unique && n.ExceptionStatus == TransactionExceptionStatus.None));
        }

        await harness.CalculateAsync(days);
        _output.WriteLine($"{clock.Elapsed}: calculated");
        AssertNoMismatches(Mismatches(harness, days, settings));
    }

    [Fact]
    public async Task TheScenarioFiles_AreTheOnesCommittedInTestsScenarioData()
    {
        // What each day must come to under the starting rules goes in the files too, so a person can check a run against it.
        using var harness = new ScenarioHarness([ScenarioGroup.All[0]]);
        var generated = ScenarioFiles(await harness.ApplyAsync(RuleProfile.All[0]));
        var output = Path.Combine(AppContext.BaseDirectory, "scenario-data");
        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }

        Directory.CreateDirectory(output);
        foreach (var (name, content) in generated)
        {
            File.WriteAllText(Path.Combine(output, name), content, new UTF8Encoding(false));
        }

        var committed = Path.Combine(TestsFolder(), "scenario-data");
        var differing = new List<string>();
        foreach (var (name, content) in generated)
        {
            var path = Path.Combine(committed, name);
            if (!File.Exists(path) || Normalize(File.ReadAllText(path)) != Normalize(content))
            {
                differing.Add(name);
            }
        }

        if (Directory.Exists(committed))
        {
            differing.AddRange(Directory.GetFiles(committed).Select(Path.GetFileName).Where(n => n is not null && !generated.ContainsKey(n!)).Select(n => $"{n} (no longer generated)"));
        }

        Assert.True(differing.Count == 0, $"{differing.Count} file(s) of tests/scenario-data are missing or out of date, e.g. {string.Join(", ", differing.Take(5))}. The current ones are in {output}: copy them over tests/scenario-data.");
    }

    private static void AssertNoMismatches(IReadOnlyList<string> mismatches) =>
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    /// <summary>The tests folder of the repository, found from this source file, so it does not depend on where the tests are built.</summary>
    private static string TestsFolder([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", ".."));

    /// <summary>One CSV per employee (named after the employee number), the list of employees, what each day must come to, and a description of every case.</summary>
    private static SortedDictionary<string, string> ScenarioFiles(OracleSettings startingRules)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var employees = new StringBuilder("employee_number,first_name,last_name,department,time_schedule,team,pay_basis,device_id\r\n");
        var expected = new StringBuilder("employee_number,date,weekday,case,expected_exceptions,expected_status,expected_hours_worked\r\n");
        foreach (var group in ScenarioGroup.All)
        {
            foreach (var scenario in group.CoverageCases)
            {
                var employee = group.EmployeeFor(scenario);
                var plans = ScenarioPlanner.PlanAll(group, scenario, ScenarioGroup.FirstDay, ScenarioGroup.LastDay);
                files[$"{employee.Number}.csv"] = ScenarioCsv.Build(employee.Number, plans);
                employees.Append($"{employee.Number},\"{employee.FirstName}\",\"{employee.LastName}\",\"{employee.Department}\",\"{employee.ScheduleTitle}\",{(employee.TeamTitle is null ? string.Empty : $"\"{employee.TeamTitle}\"")},{(group.OnDemand ? "Hourly" : "Scheduled")},{ScenarioCsv.DeviceId}\r\n");

                foreach (var plan in plans.Where(p => p.Shift is not null || p.Punches.Count > 0))
                {
                    var day = ScenarioOracle.Expect(plan, startingRules);
                    var exceptions = day.Exceptions.Count == 0 ? "none" : string.Join(" + ", day.Exceptions.OrderBy(e => e));
                    var status = day.HasRecord ? (day.NeedsReview ? "Pending review" : "Approved") : "no record";
                    var worked = day.WorkedMinutes is { } minutes ? $"{minutes / 60}:{minutes % 60:00}" : string.Empty;
                    expected.Append($"{employee.Number},{plan.Date:yyyy-MM-dd},{plan.Date:ddd},{ScenarioPlanner.CodeOf(plan.Kind)},{exceptions},{status},{worked}\r\n");
                }
            }
        }

        files["employees.csv"] = employees.ToString();
        files["expected-results.csv"] = expected.ToString();
        files["README.md"] = Readme();
        return files;
    }

    private static string Readme()
    {
        var text = new StringBuilder();
        text.AppendLine("# Scenario attendance files");
        text.AppendLine();
        text.AppendLine("One Generic CSV file per employee, 2026-04-01 to 2026-05-30. There is an employee for every time schedule (and every team of it that works)");
        text.AppendLine("the template seeder makes, and the file of each holds every kind of day below, one after another on the days the schedule works, so a");
        text.AppendLine("single upload exercises everything. A schedule that works few days cannot fit all of them in one file (a weekend schedule works 17 days of");
        text.AppendLine("the period, each team of the alternate-weekend schedule 8 or 9), so those have two or three employees (`ALL1`, `ALL2`...) that together have every kind.");
        text.AppendLine("On a day the schedule does not work, the employee sometimes punches in and out anyway (`OFF_DAY`).");
        text.AppendLine();
        text.AppendLine("- `employees.csv` lists the employees. Load it on *System → Load test employees*: it makes them, puts each on its schedule and team and maps its number");
        text.AppendLine("  to device `SCN-1` (made if the tenant has none), so the files can be uploaded as they are (*Upload attendance*). Remove them again, with all their data,");
        text.AppendLine("  on *System → Delete test employees* with the same file. Nothing creates them at startup.");
        text.AppendLine("- `expected-results.csv` says, for every day, which kind of day it is and what the **starting rules** make of it: the exceptions, whether it waits");
        text.AppendLine("  for review or is approved, and the hours worked. Other rules give other answers.");
        text.AppendLine("- The employee number is the initials of the schedule (and of the team), a dash and `ALL`. The punches vary from day to day but are the same every run.");
        text.AppendLine("- These files are generated by `ScheduleScenarioTests`, which also checks what the engine makes of them against `expected-results.csv`'s rule.");
        text.AppendLine();
        text.AppendLine("| Kind of day | What happens |");
        text.AppendLine("|---|---|");
        foreach (var scenario in AttendanceScenarioCatalog.Cases.Where(c => !c.Code.EndsWith("MIXED", StringComparison.Ordinal)))
        {
            text.AppendLine($"| `{scenario.Code}` | {scenario.Description} |");
        }

        text.AppendLine();
        text.AppendLine("| Schedule | Team | Number prefix | Employees |");
        text.AppendLine("|---|---|---|---|");
        foreach (var group in ScenarioGroup.All)
        {
            text.AppendLine($"| {group.ScheduleTitle} | {group.TeamTitle ?? "(everyone)"} | `{group.Prefix}` | {group.CoverageCases.Count} |");
        }

        return text.ToString();
    }

    private static List<(ScenarioHarness.Person Person, DayPlan Plan)> Plans(ScenarioHarness harness, DateTime first, DateTime last) =>
        harness.People
            .SelectMany(person => ScenarioPlanner.PlanAll(person.Group, person.Case, first, last).Select(plan => (person, plan)))
            .Where(d => d.plan.Shift is not null || d.plan.Punches.Count > 0)
            .ToList();

    /// <summary>The days where the engine's answer is not the oracle's, each said in a line.</summary>
    private static List<string> Mismatches(ScenarioHarness harness, IReadOnlyList<(ScenarioHarness.Person Person, DayPlan Plan)> days, OracleSettings settings)
    {
        var actual = harness.ReadDays();
        var wrong = new List<string>();
        foreach (var (person, plan) in days)
        {
            var expected = ScenarioOracle.Expect(plan, settings);
            var got = actual.GetValueOrDefault((person.EmployeeId, plan.Date)) ?? new ScenarioHarness.ActualDay(new HashSet<AttendanceExceptionType>(), 0, false, 0, 0);
            var problems = new List<string>();
            if (!expected.Exceptions.SetEquals(got.Exceptions))
            {
                problems.Add($"exceptions {Describe(expected.Exceptions)} expected, {Describe(got.Exceptions)} found");
            }

            if (expected.HasRecord != (got.Records > 0))
            {
                problems.Add(expected.HasRecord ? "no record was made" : "a record was made");
            }
            else if (expected.HasRecord && expected.NeedsReview != got.NeedsReview)
            {
                problems.Add(expected.NeedsReview ? "should wait for review but is approved" : "should be approved but waits for review");
            }

            if (expected.WorkedMinutes is { } worked && worked != got.WorkedMinutes)
            {
                problems.Add($"{worked} minutes worked expected, {got.WorkedMinutes} found");
            }

            if (expected.OvertimeMinutes is { } overtime && overtime != got.OvertimeMinutes)
            {
                problems.Add($"{overtime} minutes of overtime expected, {got.OvertimeMinutes} found");
            }

            if (problems.Count > 0)
            {
                var punches = string.Join(" ", plan.Punches.Select(p => $"{p.Type}@{p.At:MM-dd HH:mm}"));
                wrong.Add($"{person.Number} {plan.Date:yyyy-MM-dd} {plan.Kind} [{punches}]: {string.Join("; ", problems)}");
            }
        }

        if (wrong.Count > 0)
        {
            wrong.Insert(0, $"{wrong.Count} of {days.Count} day(s) differ from what the rules say (the first {Math.Min(25, wrong.Count)} follow):");
            return wrong.Take(26).ToList();
        }

        return wrong;
    }

    private static string Describe(IEnumerable<AttendanceExceptionType> types) => types.Any() ? $"{{{string.Join(", ", types.OrderBy(t => t))}}}" : "none";
}
