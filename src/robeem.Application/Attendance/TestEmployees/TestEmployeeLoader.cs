using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance.TestEmployees;

public sealed class TestEmployeeLoader : ITestEmployeeLoader
{
    public const string DefaultTimeZone = "Asia/Beirut";

    /// <summary>The integration made for a device the file names and the tenant does not have.</summary>
    public const string IntegrationName = "Test employees";

    private const int MaxChars = 2_000_000;

    private const int MaxEmployees = 5000;

    private static readonly DateTime DefaultHireDate = new(2026, 1, 1);

    private static readonly IReadOnlyList<TestEmployeeColumn> Columns =
    [
        new() { Name = "employee_number", Required = true, Description = "The employee's number: unique, up to 50 characters. An employee the tenant already has with this number is left as it is." },
        new() { Name = "first_name", Required = true, Description = "First name (up to 100 characters)." },
        new() { Name = "last_name", Required = true, Description = "Last name (up to 100 characters)." },
        new() { Name = "department", Description = "Department." },
        new() { Name = "time_schedule", Description = "The name of a time schedule the tenant has. Empty: not on a schedule." },
        new() { Name = "team", Description = "The name of a team of that schedule. Empty: the schedule's team for everyone. An employee paid by the schedule works the team's shifts and the everyone-team's." },
        new() { Name = "pay_basis", Description = "Scheduled (the default) or Hourly. Only an hourly employee can be on an on-demand schedule." },
        new() { Name = "device_id", Description = "The id of the punch device the employee's punches come from (the device_id column of an attendance file). The employee number is mapped to it, so a file of punches finds the employee. A device the tenant does not have is made." },
        new() { Name = "email", Description = "Email address." },
        new() { Name = "job_title", Description = "Job title." },
        new() { Name = "hire_date", Description = "yyyy-MM-dd. Empty: 2026-01-01." },
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["employeenumber"] = "employee_number",
        ["employeeid"] = "employee_number",
        ["number"] = "employee_number",
        ["firstname"] = "first_name",
        ["lastname"] = "last_name",
        ["department"] = "department",
        ["timeschedule"] = "time_schedule",
        ["schedule"] = "time_schedule",
        ["team"] = "team",
        ["paybasis"] = "pay_basis",
        ["deviceid"] = "device_id",
        ["device"] = "device_id",
        ["email"] = "email",
        ["jobtitle"] = "job_title",
        ["hiredate"] = "hire_date",
    };

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;
    private readonly IOptions<TestEmployeeOptions> _options;

    public TestEmployeeLoader(IRobeemDbContext db, IAttendanceAuditTrail audit, IOptions<TestEmployeeOptions> options)
    {
        _db = db;
        _audit = audit;
        _options = options;
    }

    public async Task<TestEmployeeSummary> GetSummaryAsync(CancellationToken cancellationToken) => new()
    {
        TenantId = _db.CurrentTenantId,
        Enabled = _options.Value.Enabled,
        EmployeeCount = await _db.Profiles.CountAsync(cancellationToken),
        DefaultTimeZone = DefaultTimeZone,
        Columns = Columns,
    };

    public async Task<TestEmployeeLoadOutcome> LoadAsync(Stream csv, string deviceTimeZone, CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled)
        {
            return TestEmployeeLoadOutcome.Disabled();
        }

        string text;
        using (var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        {
            text = await reader.ReadToEndAsync(cancellationToken);
        }

        if (text.Length > MaxChars)
        {
            return TestEmployeeLoadOutcome.Invalid(["The file is too big for test employees (at most 2 million characters)."]);
        }

        var records = TestEmployeeCsv.Parse(text);
        if (records.Count == 0)
        {
            return TestEmployeeLoadOutcome.Invalid(["The file is empty: it needs a header row and one row for each employee."]);
        }

        var header = ReadHeader(records[0], out var headerErrors);
        if (headerErrors.Count > 0)
        {
            return TestEmployeeLoadOutcome.Invalid(headerErrors);
        }

        if (records.Count - 1 > MaxEmployees)
        {
            return TestEmployeeLoadOutcome.Invalid([$"The file has {records.Count - 1} employees; at most {MaxEmployees} can be loaded at once."]);
        }

        var context = await ReadContextAsync(cancellationToken);
        var errors = new List<string>();
        var rows = new List<Row>();
        var skipped = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < records.Count; i++)
        {
            var row = ReadRow(i + 1, records[i], header, context, errors);
            if (row is null)
            {
                continue;
            }

            if (!seen.Add(row.Number))
            {
                errors.Add($"Line {row.Line} ({row.Number}): the employee number is in the file twice.");
            }
            else if (context.ExistingNumbers.Contains(row.Number))
            {
                skipped++;
            }
            else
            {
                rows.Add(row);
            }
        }

        var devices = await ReadDevicesAsync(rows, errors, cancellationToken);
        var newDevices = rows.Select(r => r.DeviceId).Where(d => d is not null && !devices.ContainsKey(d)).Distinct().ToList();
        if (newDevices.Count > 0 && !IsKnownTimeZone(deviceTimeZone))
        {
            errors.Add($"'{deviceTimeZone}' is not a time zone (an IANA name such as {DefaultTimeZone}); it is needed for the device(s) the file names that the tenant does not have: {string.Join(", ", newDevices)}.");
        }

        if (errors.Count > 0)
        {
            return TestEmployeeLoadOutcome.Invalid(errors);
        }

        // Everything is right: make it, in one save, so a file is loaded whole or not at all.
        var created = new List<string>();
        AttendanceIntegration? integration = null;
        foreach (var id in newDevices)
        {
            integration ??= await _db.AttendanceIntegrations.FirstOrDefaultAsync(i => i.DisplayName == IntegrationName, cancellationToken)
                ?? _db.AttendanceIntegrations.Add(new AttendanceIntegration { ConnectorKey = "generic-csv", DisplayName = IntegrationName, TimeZoneId = deviceTimeZone }).Entity;
            var device = new AttendanceDevice { Integration = integration, ExternalDeviceId = id!, Name = $"Test clock {id}", TimeZoneId = deviceTimeZone };
            _db.AttendanceDevices.Add(device);
            devices[id!] = device;
            created.Add($"{device.Name} ({id}, {deviceTimeZone})");
        }

        var (assigned, mapped) = (0, 0);
        foreach (var row in rows)
        {
            var profile = new Profile
            {
                EmployeeNumber = row.Number,
                FirstName = row.FirstName,
                LastName = row.LastName,
                Title = $"{row.FirstName} {row.LastName}",
                Email = row.Email,
                Department = row.Department,
                JobTitle = row.JobTitle,
                HireDate = row.HireDate,
                PayBasis = row.Pay,
            };
            _db.Profiles.Add(profile);
            if (row.Schedule is not null)
            {
                _db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { Employee = profile, TimeScheduleId = row.Schedule.Id, TeamId = row.Team!.Id });
                assigned++;
            }

            if (row.DeviceId is not null)
            {
                _db.ExternalEmployeeIdMappings.Add(new ExternalEmployeeIdMapping { Device = devices[row.DeviceId], ExternalEmployeeId = row.Number, Employee = profile });
                mapped++;
            }
        }

        var devicesMade = created.Count > 0 ? $"; devices made: {string.Join(", ", created)}." : ".";
        var details = $"Loaded test employees into tenant '{_db.CurrentTenantId}': {rows.Count} created, {skipped} already there, {assigned} put on a time schedule, {mapped} mapped to a device{devicesMade}";
        _audit.Record(AttendanceAuditActions.TestEmployeesLoaded, "Tenant", null, details);
        await _db.SaveChangesAsync(cancellationToken);

        return TestEmployeeLoadOutcome.Done(new TestEmployeeLoadResult { Created = rows.Count, Skipped = skipped, Assigned = assigned, Mapped = mapped, DevicesCreated = created });
    }

    private static bool IsKnownTimeZone(string id)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return !string.IsNullOrWhiteSpace(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The columns of the header, by their standard name; the file must have the three required ones.</summary>
    private static Dictionary<string, int> ReadHeader(string[] header, out List<string> errors)
    {
        errors = [];
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++)
        {
            if (Aliases.TryGetValue(TestEmployeeCsv.Key(header[i]), out var name) && !columns.TryAdd(name, i))
            {
                errors.Add($"The column {name} is in the file twice.");
            }
        }

        foreach (var required in Columns.Where(c => c.Required))
        {
            if (!columns.ContainsKey(required.Name))
            {
                errors.Add($"The header row has no {required.Name} column. The columns are: {string.Join(", ", Columns.Select(c => c.Name))}.");
            }
        }

        return columns;
    }

    private static string? Cell(string[] record, Dictionary<string, int> header, string column) =>
        header.TryGetValue(column, out var index) && index < record.Length && record[index].Length > 0 ? record[index] : null;

    private static Row? ReadRow(int line, string[] record, Dictionary<string, int> header, Context context, List<string> errors)
    {
        var number = Cell(record, header, "employee_number");
        var label = $"Line {line}" + (number is null ? string.Empty : $" ({number})");
        var problems = new List<string>();

        if (number is null)
        {
            problems.Add("the employee number is empty");
        }
        else if (number.Length > 50)
        {
            problems.Add("the employee number is longer than 50 characters");
        }

        var first = Cell(record, header, "first_name");
        var last = Cell(record, header, "last_name");
        if (first is null || first.Length > 100)
        {
            problems.Add("the first name is empty or longer than 100 characters");
        }

        if (last is null || last.Length > 100)
        {
            problems.Add("the last name is empty or longer than 100 characters");
        }

        var pay = PayBasis.Scheduled;
        if (Cell(record, header, "pay_basis") is { } payText && (!Enum.TryParse(payText, ignoreCase: true, out pay) || !Enum.IsDefined(pay)))
        {
            problems.Add($"pay_basis '{payText}' is not Scheduled or Hourly");
        }

        var hire = DefaultHireDate;
        if (Cell(record, header, "hire_date") is { } hireText && !DateTime.TryParseExact(hireText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out hire))
        {
            problems.Add($"hire_date '{hireText}' is not a date written yyyy-MM-dd");
        }

        var (schedule, team) = ReadSchedule(record, header, context, pay, problems);
        foreach (var (column, max) in new[] { ("department", 100), ("job_title", 100), ("email", 255) })
        {
            if (Cell(record, header, column) is { } value && value.Length > max)
            {
                problems.Add($"{column} is longer than {max} characters");
            }
        }

        if (problems.Count > 0)
        {
            errors.AddRange(problems.Select(p => $"{label}: {p}."));
            return null;
        }

        return new Row(line, number!, first!, last!, Cell(record, header, "department"), Cell(record, header, "email"), Cell(record, header, "job_title"), hire, pay, schedule, team, Cell(record, header, "device_id"));
    }

    private static (TimeSchedule? Schedule, TimeScheduleTeam? Team) ReadSchedule(string[] record, Dictionary<string, int> header, Context context, PayBasis pay, List<string> problems)
    {
        var scheduleName = Cell(record, header, "time_schedule");
        var teamName = Cell(record, header, "team");
        if (scheduleName is null)
        {
            if (teamName is not null)
            {
                problems.Add("a team is given with no time_schedule");
            }

            return (null, null);
        }

        if (!context.Schedules.TryGetValue(scheduleName.ToLowerInvariant(), out var schedule))
        {
            problems.Add($"the time schedule '{scheduleName}' does not exist (the tenant has: {string.Join(", ", context.Schedules.Values.Select(s => s.Title).Order())})");
            return (null, null);
        }

        var teams = context.Teams.Where(t => t.TimeScheduleId == schedule.Id).ToList();
        var team = teamName is null
            ? teams.Where(t => t.AllInclusive).OrderBy(t => t.Id).FirstOrDefault()
            : teams.FirstOrDefault(t => string.Equals(t.Title, teamName, StringComparison.OrdinalIgnoreCase));
        if (team is null)
        {
            problems.Add(teamName is null
                ? $"the time schedule '{schedule.Title}' has no team for everyone"
                : $"the time schedule '{schedule.Title}' has no team '{teamName}' (it has: {string.Join(", ", teams.Select(t => t.Title).Order())})");
        }

        if (context.OnDemand.Contains(schedule.Id) && pay != PayBasis.Hourly)
        {
            problems.Add($"'{schedule.Title}' is on demand: only an employee with pay_basis Hourly can be on it");
        }

        return (schedule, team);
    }

    /// <summary>The device each row names that the tenant has already, and a row's number that is mapped there to somebody else already is an error.</summary>
    private async Task<Dictionary<string, AttendanceDevice>> ReadDevicesAsync(List<Row> rows, List<string> errors, CancellationToken cancellationToken)
    {
        var devices = new Dictionary<string, AttendanceDevice>(StringComparer.Ordinal);
        foreach (var id in rows.Select(r => r.DeviceId).Where(d => d is not null).Distinct())
        {
            var device = await _db.AttendanceDevices.OrderBy(d => d.Id).FirstOrDefaultAsync(d => d.ExternalDeviceId == id, cancellationToken);
            if (device is null)
            {
                continue;
            }

            devices[id!] = device;
            var taken = await _db.ExternalEmployeeIdMappings.Where(m => m.DeviceId == device.Id).Select(m => m.ExternalEmployeeId).ToListAsync(cancellationToken);
            errors.AddRange(rows
                .Where(r => r.DeviceId == id && taken.Contains(r.Number, StringComparer.Ordinal))
                .Select(r => $"Line {r.Line} ({r.Number}): the device {id} already maps the id {r.Number} to another employee."));
        }

        return devices;
    }

    private async Task<Context> ReadContextAsync(CancellationToken cancellationToken) => new(
        (await _db.Profiles.Select(p => p.EmployeeNumber).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal),
        (await _db.TimeSchedules.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(s => s.Title.ToLowerInvariant()),
        await _db.TimeScheduleTeams.AsNoTracking().ToListAsync(cancellationToken),
        (await _db.OnDemandTimeSchedules.Select(o => o.TimeScheduleId).ToListAsync(cancellationToken)).ToHashSet());

    private sealed record Context(HashSet<string> ExistingNumbers, Dictionary<string, TimeSchedule> Schedules, List<TimeScheduleTeam> Teams, HashSet<int> OnDemand);

    private sealed record Row(
        int Line,
        string Number,
        string FirstName,
        string LastName,
        string? Department,
        string? Email,
        string? JobTitle,
        DateTime HireDate,
        PayBasis Pay,
        TimeSchedule? Schedule,
        TimeScheduleTeam? Team,
        string? DeviceId);
}
