using Microsoft.EntityFrameworkCore;
using robeem.Application.Attendance;
using robeem.Application.Attendance.Seeding;
using robeem.Application.HR.Absence;
using robeem.Application.HR.Seeding;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance.Scenarios;

/// <summary>
/// A tenant with the time schedules the template seeder makes, one employee for every case of every schedule (and team), a device the
/// employees are mapped to, and a clock set after the whole period. It puts the days' punches in and calculates them with the real engine.
/// </summary>
internal sealed class ScenarioHarness : IDisposable
{
    private readonly int _integrationId;
    private readonly int _deviceId;
    private readonly int _rawId;

    /// <param name="groups">The schedules (and teams) to make employees for.</param>
    /// <param name="coverage">Whether the employees are the ones whose files hold every kind of day, instead of one employee for each kind.</param>
    public ScenarioHarness(IReadOnlyList<ScenarioGroup> groups, bool coverage = false)
    {
        Database.Store(AttendanceTestData.Connector("generic-csv", "Generic CSV", adapterKey: "generic-csv"));
        Database.SeedAttendanceCatalog();

        using var db = Database.CreateContext(TestDatabase.TenantA);
        new ScheduleTemplateSeedContributor().SeedAsync(db, CancellationToken.None).GetAwaiter().GetResult();
        db.SaveChanges();

        var integration = new AttendanceIntegration { ConnectorKey = "generic-csv", DisplayName = "Scenario files" };
        var device = new AttendanceDevice { Integration = integration, ExternalDeviceId = ScenarioCsv.DeviceId, Name = "Scenario clock", TimeZoneId = "UTC" };
        var raw = new RawAttendanceTransaction
        {
            Integration = integration,
            ImportBatch = new AttendanceProcessingBatch { Integration = integration, BatchType = AttendanceBatchType.ManualUpload },
            TransactionTimestampRaw = "-",
            RawPayload = "{}",
            SourceSystem = "generic-csv",
            RecordHash = "scenario",
        };
        db.AddRange(device, raw);
        db.SaveChanges();
        (_integrationId, _deviceId, _rawId) = (integration.Id, device.Id, raw.Id);

        foreach (var group in groups)
        {
            var schedule = db.TimeSchedules.Single(s => s.Title == group.ScheduleTitle);
            var team = db.TimeScheduleTeams.Where(t => t.TimeScheduleId == schedule.Id && (group.TeamTitle == null ? t.AllInclusive : t.Title == group.TeamTitle)).OrderBy(t => t.Id).First();
            foreach (var scenario in coverage ? group.CoverageCases : group.Cases)
            {
                var employee = group.EmployeeFor(scenario);
                var profile = new Profile
                {
                    EmployeeNumber = employee.Number,
                    FirstName = employee.FirstName,
                    LastName = employee.LastName,
                    Title = $"{employee.FirstName} {employee.LastName}",
                    Department = employee.Department,
                    HireDate = new DateTime(2026, 1, 1),
                    PayBasis = group.OnDemand ? PayBasis.Hourly : PayBasis.Scheduled,
                };
                db.Profiles.Add(profile);
                db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { Employee = profile, TimeScheduleId = schedule.Id, TeamId = team.Id });
                db.ExternalEmployeeIdMappings.Add(new ExternalEmployeeIdMapping { DeviceId = device.Id, ExternalEmployeeId = employee.Number, Employee = profile });
                People.Add(new Person(group, scenario, employee.Number, profile));
            }
        }

        db.SaveChanges();
        foreach (var person in People)
        {
            person.EmployeeId = person.Profile.Id;
        }
    }

    public TestDatabase Database { get; } = new();

    /// <summary>Gets the engine's clock: after the period, so every grace has passed and no shift is still to come.</summary>
    public FixedTimeProvider Clock { get; } = new(new DateTime(2026, 6, 15));

    public List<Person> People { get; } = [];

    public int IntegrationId => _integrationId;

    public void Dispose() => Database.Dispose();

    /// <summary>
    /// Sets the rules and policy of a profile and returns the standard rules in force afterwards, read back from the service
    /// (so the tests compare against what a tenant would see on the Rules page).
    /// </summary>
    public async Task<OracleSettings> ApplyAsync(RuleProfile profile)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.AttendancePolicies.Add(new AttendancePolicy
        {
            Title = "Default",
            UnlabeledPunchInterpretationMode = profile.Sequential ? UnlabeledPunchInterpretationMode.Sequential : UnlabeledPunchInterpretationMode.Off,
            RequiresOvertimePreApproval = profile.PreApproval,
        });
        await db.SaveChangesAsync();

        var service = new AttendanceRuleService(db, new AttendanceAuditTrail(db, new SystemCurrentUser(), Clock), new SystemCurrentUser(), Clock);
        var settings = (await service.GetSettingsAsync(null, CancellationToken.None))!.Settings with { };
        profile.Change(settings);
        if (settings != (await service.GetSettingsAsync(null, CancellationToken.None))!.Settings)
        {
            var saved = await service.SaveSettingsAsync(new RuleSettingsSaveRequest { ExpectedVersion = 0, Settings = settings }, CancellationToken.None);
            if (!saved.Succeeded)
            {
                throw new InvalidOperationException($"The rules of '{profile.Name}' were refused: {string.Join("; ", saved.Errors.Select(e => $"{e.Key}: {e.Value}"))}");
            }
        }

        return new OracleSettings((await service.GetSettingsAsync(null, CancellationToken.None))!.Settings, profile.Sequential, profile.PreApproval);
    }

    /// <summary>
    /// Stores every plan's punches as normalized punches, as normalization would have made them from the files. A punch with no type is
    /// normalized only when the policy reads them in order; otherwise the row fails and there is no normalized punch.
    /// </summary>
    public void StorePunches(IEnumerable<(Person Person, DayPlan Plan)> days, bool unlabeledAreKept)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        foreach (var (person, plan) in days)
        {
            foreach (var punch in plan.Punches.Where(p => unlabeledAreKept || p.Type != NormalizedEventType.Unknown))
            {
                var utc = new DateTimeOffset(punch.At, TimeSpan.Zero);
                db.NormalizedAttendanceTransactions.Add(new NormalizedAttendanceTransaction
                {
                    EmployeeId = person.EmployeeId,
                    DeviceId = _deviceId,
                    IntegrationId = _integrationId,
                    RawTransactionId = _rawId,
                    AttendanceDate = punch.At.Date,
                    EventTimestampUtc = utc,
                    EventTimestampLocal = punch.At,
                    TimeZoneId = "UTC",
                    NormalizedEventType = punch.Type,
                    ConfidenceStatus = punch.Type == NormalizedEventType.Unknown ? EventConfidenceStatus.Ambiguous : EventConfidenceStatus.Certain,
                    DuplicateBucketTimestampUtc = utc,
                });
            }
        }

        db.SaveChanges();
    }

    /// <summary>Calculates every given day with the engine, one employee at a time.</summary>
    public async Task CalculateAsync(IEnumerable<(Person Person, DayPlan Plan)> days)
    {
        foreach (var employee in days.GroupBy(d => d.Person.EmployeeId))
        {
            using var db = Database.CreateContext(TestDatabase.TenantA);
            var engine = CreateEngine(db);
            foreach (var (_, plan) in employee)
            {
                await engine.ProcessEmployeeDayAsync(employee.Key, plan.Date, CancellationToken.None);
            }
        }
    }

    public AttendanceProcessingEngine CreateEngine(RobeemDbContext db) =>
        new(db, new ScheduleResolutionService(db), new AttendancePolicyResolver(db), new AttendanceAuditTrail(db, new SystemCurrentUser(), Clock), Clock, new HrAbsenceCalendar(db), new RuleSetProvider(db));

    /// <summary>What the engine made of every day: the exceptions still open and the current records, by employee and date.</summary>
    public Dictionary<(int EmployeeId, DateTime Date), ActualDay> ReadDays()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var exceptions = db.AttendanceExceptions.AsNoTracking()
            .Where(e => e.Status != AttendanceExceptionStatus.Closed)
            .Select(e => new { e.EmployeeId, e.AttendanceDate, e.ExceptionType })
            .ToList();
        var records = db.AttendanceRecords.AsNoTracking()
            .Where(r => r.IsCurrent)
            .Select(r => new { r.EmployeeId, r.AttendanceDate, r.Status, r.WorkingHours, r.OvertimeMinutes })
            .ToList();

        var days = new Dictionary<(int EmployeeId, DateTime Date), ActualDay>();
        var keys = exceptions.Select(e => (e.EmployeeId, Date: e.AttendanceDate.Date)).Concat(records.Select(r => (r.EmployeeId, Date: r.AttendanceDate.Date))).Distinct();
        foreach (var (employeeId, date) in keys)
        {
            var dayRecords = records.Where(r => r.EmployeeId == employeeId && r.AttendanceDate.Date == date).ToList();
            days[(employeeId, date)] = new ActualDay(
                exceptions.Where(e => e.EmployeeId == employeeId && e.AttendanceDate.Date == date).Select(e => e.ExceptionType).ToHashSet(),
                dayRecords.Count,
                dayRecords.Any(r => r.Status == AttendanceRecordStatus.PendingReview),
                (int)Math.Round(dayRecords.Sum(r => r.WorkingHours.TotalMinutes)),
                dayRecords.Sum(r => r.OvertimeMinutes));
        }

        return days;
    }

    /// <summary>One employee of the scenario data.</summary>
    internal sealed class Person
    {
        public Person(ScenarioGroup group, ScenarioCase scenario, string number, Profile profile)
        {
            Group = group;
            Case = scenario;
            Number = number;
            Profile = profile;
        }

        public ScenarioGroup Group { get; }

        public ScenarioCase Case { get; }

        public string Number { get; }

        public Profile Profile { get; }

        public int EmployeeId { get; set; }
    }

    /// <summary>What the engine made of one day.</summary>
    internal sealed record ActualDay(IReadOnlySet<AttendanceExceptionType> Exceptions, int Records, bool NeedsReview, int WorkedMinutes, int OvertimeMinutes);
}
