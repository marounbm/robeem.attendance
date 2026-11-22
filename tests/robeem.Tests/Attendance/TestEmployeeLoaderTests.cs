using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Application.Attendance.TestEmployees;
using robeem.Application.HR.Seeding;
using robeem.Application.Security;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>Loading test employees from a CSV file: the employees, their schedule and team, and their numbers mapped to a device.</summary>
public sealed class TestEmployeeLoaderTests : IDisposable
{
    private const string Header = "employee_number,first_name,last_name,department,time_schedule,team,pay_basis,device_id\r\n";

    private readonly TestDatabase _database = new();

    public TestEmployeeLoaderTests()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        new ScheduleTemplateSeedContributor().SeedAsync(db, CancellationToken.None).GetAwaiter().GetResult();
        db.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task WhereTheHostHasNotTurnedItOn_NothingIsLoaded()
    {
        var outcome = await LoadAsync(Header + "T-1,Ada,Lovelace,,,,,\r\n", enabled: false);

        Assert.Equal(TestEmployeeLoadStatus.Disabled, outcome.Status);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.Profiles);
        Assert.False((await Summary(enabled: false)).Enabled);
    }

    [Fact]
    public async Task TheEmployeesFileOfTheScenarioData_IsLoaded_OnTheirTeams_MappedToADeviceMadeForThem_AndAuditedOnce()
    {
        var file = await File.ReadAllTextAsync(Path.Combine(TestsFolder(), "scenario-data", "employees.csv"));

        var outcome = await LoadAsync(file);

        Assert.Equal(TestEmployeeLoadStatus.Done, outcome.Status);
        var result = outcome.Result!;
        Assert.Equal((17, 0, 17, 17), (result.Created, result.Skipped, result.Assigned, result.Mapped));
        Assert.Equal(["Test clock SCN-1 (SCN-1, Asia/Beirut)"], result.DevicesCreated);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var night = db.TimeScheduleEmployees.Single(a => a.Employee!.EmployeeNumber == "CO7D-NT-ALL");
        Assert.Equal("Night Team", db.TimeScheduleTeams.Single(t => t.Id == night.TeamId).Title);
        var weekendB = db.TimeScheduleEmployees.Single(a => a.Employee!.EmployeeNumber == "AWS-WB-ALL2");
        Assert.Equal("Weekend B", db.TimeScheduleTeams.Single(t => t.Id == weekendB.TeamId).Title);
        var standard = db.TimeScheduleEmployees.Single(a => a.Employee!.EmployeeNumber == "SW-ALL");
        Assert.True(db.TimeScheduleTeams.Single(t => t.Id == standard.TeamId).AllInclusive);
        Assert.Equal(PayBasis.Hourly, db.Profiles.Single(p => p.EmployeeNumber == "ODHS-ALL").PayBasis);

        var device = db.AttendanceDevices.Single();
        Assert.Equal(("SCN-1", "Asia/Beirut"), (device.ExternalDeviceId, device.TimeZoneId));
        Assert.Equal(17, db.ExternalEmployeeIdMappings.Count(m => m.DeviceId == device.Id));
        Assert.Equal("Test employees", db.AttendanceIntegrations.Single().DisplayName);

        var entry = Assert.Single(db.AttendanceAuditLogs);
        Assert.Equal((AttendanceAuditActions.TestEmployeesLoaded, "Tenant"), (entry.Action, entry.TargetType));
        Assert.Contains("17 created", entry.Details);
    }

    [Fact]
    public async Task LoadingTheSameFileAgain_ChangesNothing()
    {
        var file = Header + "T-1,Ada,Lovelace,Sales,Standard Week,,Scheduled,SCN-1\r\nT-2,Alan,Turing,Sales,Standard Week,,,SCN-1\r\n";
        await LoadAsync(file);

        var again = await LoadAsync(file);

        Assert.Equal(TestEmployeeLoadStatus.Done, again.Status);
        Assert.Equal((0, 2, 0, 0), (again.Result!.Created, again.Result.Skipped, again.Result.Assigned, again.Result.Mapped));
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal((2, 2, 1), (db.Profiles.Count(), db.ExternalEmployeeIdMappings.Count(), db.AttendanceDevices.Count()));
    }

    [Fact]
    public async Task AFileWithAWrongRow_LoadsNothing_AndSaysEveryWrongRow()
    {
        var file = Header
            + "T-1,Ada,Lovelace,,Standard Week,,Scheduled,\r\n"
            + "T-2,Alan,Turing,,No Such Schedule,,,\r\n"
            + "T-3,Grace,Hopper,,Standard Week,Night Team,,\r\n"
            + "T-4,Linus,Pauling,,On Demand (Hourly Staff),,Scheduled,\r\n"
            + "T-5,,Noether,,,,,\r\n"
            + "T-6,Edsger,Dijkstra,,,,Piecework,\r\n"
            + "T-1,Ada,Again,,,,,\r\n";

        var outcome = await LoadAsync(file);

        Assert.Equal(TestEmployeeLoadStatus.Invalid, outcome.Status);
        Assert.Contains(outcome.Errors, e => e.StartsWith("Line 3 (T-2)", StringComparison.Ordinal) && e.Contains("'No Such Schedule' does not exist"));
        Assert.Contains(outcome.Errors, e => e.StartsWith("Line 4 (T-3)", StringComparison.Ordinal) && e.Contains("has no team 'Night Team'"));
        Assert.Contains(outcome.Errors, e => e.StartsWith("Line 5 (T-4)", StringComparison.Ordinal) && e.Contains("on demand"));
        Assert.Contains(outcome.Errors, e => e.StartsWith("Line 6 (T-5)", StringComparison.Ordinal) && e.Contains("first name"));
        Assert.Contains(outcome.Errors, e => e.StartsWith("Line 7 (T-6)", StringComparison.Ordinal) && e.Contains("Piecework"));
        Assert.Contains(outcome.Errors, e => e.StartsWith("Line 8 (T-1)", StringComparison.Ordinal) && e.Contains("twice"));
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.Profiles);
        Assert.Empty(db.AttendanceDevices);
        Assert.Empty(db.AttendanceAuditLogs);
    }

    [Fact]
    public async Task AFileWithoutTheRequiredColumns_IsRefused_AndColumnNamesNeedNotMatchInCaseOrPunctuation()
    {
        var missing = await LoadAsync("Employee Number,Name\r\nT-1,Ada\r\n");
        Assert.Equal(TestEmployeeLoadStatus.Invalid, missing.Status);
        Assert.Contains(missing.Errors, e => e.Contains("no first_name column"));

        var loose = await LoadAsync("﻿EMPLOYEE NUMBER,First-Name,LAST name,Time Schedule\r\nT-1,Ada,Lovelace,standard WEEK\r\n");
        Assert.Equal(TestEmployeeLoadStatus.Done, loose.Status);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal("Standard Week", db.TimeSchedules.Single(s => s.Id == db.TimeScheduleEmployees.Single().TimeScheduleId).Title);
    }

    [Fact]
    public async Task QuotedFields_MayHoldCommasQuotesAndLineBreaks()
    {
        var outcome = await LoadAsync(Header + "T-1,Ada,\"Lovelace, \"\"the Countess\"\"\",\"Research\r\nand Development\",,,,\r\n");

        Assert.Equal(TestEmployeeLoadStatus.Done, outcome.Status);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var employee = db.Profiles.Single();
        Assert.Equal(("Lovelace, \"the Countess\"", "Research\r\nand Development"), (employee.LastName, employee.Department));
    }

    [Fact]
    public async Task ADeviceTheTenantHas_IsUsed_AndAnIdItAlreadyMapsIsRefused()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var integration = new AttendanceIntegration { ConnectorKey = "generic-csv", DisplayName = "HQ" };
            var device = new AttendanceDevice { Integration = integration, ExternalDeviceId = "DEV-9", Name = "Front door", TimeZoneId = "UTC" };
            var other = new Profile { EmployeeNumber = "X-1", FirstName = "Other", LastName = "Person", Title = "Other Person" };
            db.ExternalEmployeeIdMappings.Add(new ExternalEmployeeIdMapping { Device = device, ExternalEmployeeId = "T-2", Employee = other });
            db.SaveChanges();
        }

        var refused = await LoadAsync(Header + "T-1,Ada,Lovelace,,,,,DEV-9\r\nT-2,Alan,Turing,,,,,DEV-9\r\n");
        Assert.Equal(TestEmployeeLoadStatus.Invalid, refused.Status);
        Assert.Contains(refused.Errors, e => e.StartsWith("Line 3 (T-2)", StringComparison.Ordinal) && e.Contains("already maps the id T-2"));

        var used = await LoadAsync(Header + "T-1,Ada,Lovelace,,,,,DEV-9\r\n");
        Assert.Equal(TestEmployeeLoadStatus.Done, used.Status);
        Assert.Empty(used.Result!.DevicesCreated);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Single(check.AttendanceDevices);
        Assert.Single(check.AttendanceIntegrations);
        Assert.Equal(2, check.ExternalEmployeeIdMappings.Count());
    }

    [Fact]
    public async Task ATimeZoneThatIsNotOne_IsRefused_OnlyWhenADeviceHasToBeMade()
    {
        var needed = await LoadAsync(Header + "T-1,Ada,Lovelace,,,,,DEV-1\r\n", timeZone: "Nowhere/Land");
        Assert.Equal(TestEmployeeLoadStatus.Invalid, needed.Status);
        Assert.Contains(needed.Errors, e => e.Contains("'Nowhere/Land' is not a time zone"));

        var notNeeded = await LoadAsync(Header + "T-1,Ada,Lovelace,,,,,\r\n", timeZone: "Nowhere/Land");
        Assert.Equal(TestEmployeeLoadStatus.Done, notNeeded.Status);
    }

    private static string TestsFolder([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));

    private static TestEmployeeLoader Loader(robeem.Infrastructure.Persistence.RobeemDbContext db, bool enabled) =>
        new(db, new AttendanceAuditTrail(db, new SystemCurrentUser(), TimeProvider.System), Options.Create(new TestEmployeeOptions { Enabled = enabled }));

    private async Task<robeem.Contracts.Attendance.TestEmployeeSummary> Summary(bool enabled)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await Loader(db, enabled).GetSummaryAsync(CancellationToken.None);
    }

    private async Task<TestEmployeeLoadOutcome> LoadAsync(string csv, bool enabled = true, string timeZone = "Asia/Beirut")
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(csv));
        return await Loader(db, enabled).LoadAsync(stream, timeZone, CancellationToken.None);
    }
}
