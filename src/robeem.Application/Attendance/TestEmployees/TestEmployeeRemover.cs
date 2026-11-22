using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance.TestEmployees;

public sealed class TestEmployeeRemover : ITestEmployeeRemover
{
    private const int MaxChars = 2_000_000;

    private const int MaxEmployees = 5000;

    private static readonly string[] NumberColumns = ["employeenumber", "employeeid", "number"];

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;
    private readonly IOptions<TestEmployeeOptions> _options;

    public TestEmployeeRemover(IRobeemDbContext db, IAttendanceAuditTrail audit, IOptions<TestEmployeeOptions> options)
    {
        _db = db;
        _audit = audit;
        _options = options;
    }

    public Task<TestEmployeeDeleteOutcome> PreviewAsync(Stream csv, CancellationToken cancellationToken) => RunAsync(csv, null, cancellationToken);

    public Task<TestEmployeeDeleteOutcome> DeleteAsync(Stream csv, string confirmation, CancellationToken cancellationToken) => RunAsync(csv, confirmation ?? string.Empty, cancellationToken);

    private async Task<TestEmployeeDeleteOutcome> RunAsync(Stream csv, string? confirmation, CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled)
        {
            return TestEmployeeDeleteOutcome.Of(TestEmployeeDeleteStatus.Disabled);
        }

        // Typed exactly, so that a click, or a request sent to the wrong tenant, can't delete anything.
        var tenantId = _db.CurrentTenantId;
        if (confirmation is not null && !string.Equals(confirmation.Trim(), tenantId, StringComparison.Ordinal))
        {
            return TestEmployeeDeleteOutcome.Of(TestEmployeeDeleteStatus.ConfirmationMismatch);
        }

        var (numbers, errors) = await ReadNumbersAsync(csv, cancellationToken);
        if (errors.Count > 0)
        {
            return TestEmployeeDeleteOutcome.Refused(TestEmployeeDeleteStatus.Invalid, errors);
        }

        var employees = await _db.Profiles.Where(p => numbers.Contains(p.EmployeeNumber)).Select(p => new { p.Id, p.EmployeeNumber }).ToListAsync(cancellationToken);
        if (employees.Count == 0)
        {
            return TestEmployeeDeleteOutcome.Refused(TestEmployeeDeleteStatus.Invalid, [$"None of the {numbers.Count} employee number(s) of the file is an employee of this tenant, so there is nothing to delete."]);
        }

        var ids = employees.Select(e => e.Id).ToList();
        var notFound = numbers.Except(employees.Select(e => e.EmployeeNumber), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        // Somebody who is not in the file and has one of these as their manager would be left pointing at nobody.
        var managed = await _db.Profiles
            .Where(p => p.ManagerId != null && ids.Contains(p.ManagerId.Value) && !ids.Contains(p.Id))
            .Select(p => p.EmployeeNumber)
            .Take(10)
            .ToListAsync(cancellationToken);
        if (managed.Count > 0)
        {
            return TestEmployeeDeleteOutcome.Refused(TestEmployeeDeleteStatus.Blocked, [$"Employees who are not in the file have one of them as their manager: {string.Join(", ", managed)}. Change their manager first, or add them to the file."]);
        }

        if (confirmation is null)
        {
            var preview = await _db.DeleteEmployeesAndTheirDataAsync(ids, dryRun: true, cancellationToken);
            return TestEmployeeDeleteOutcome.Done(new TestEmployeeDeleteResult { Deleted = false, Employees = employees.Count, NotFound = notFound, Counts = preview });
        }

        // The deletion and its audit entry are one transaction: nothing is deleted without a record that it was, and no record is made of a deletion that failed.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var deleted = await _db.DeleteEmployeesAndTheirDataAsync(ids, dryRun: false, cancellationToken);
            var list = string.Join(", ", employees.Select(e => e.EmployeeNumber).Order(StringComparer.Ordinal).Take(20)) + (employees.Count > 20 ? $" and {employees.Count - 20} more" : string.Empty);
            var what = string.Join(", ", deleted.Where(d => d.Count > 0).Select(d => $"{d.Count} {d.Name.ToLowerInvariant()}"));
            _audit.Record(
                AttendanceAuditActions.TestEmployeesDeleted,
                "Tenant",
                null,
                $"Deleted {employees.Count} employee(s) of tenant '{tenantId}' with their data ({list}): {what}. The audit log was kept.");
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TestEmployeeDeleteOutcome.Done(new TestEmployeeDeleteResult { Deleted = true, Employees = employees.Count, NotFound = notFound, Counts = deleted });
        }
        catch (DbUpdateException)
        {
            // Something else still refers to one of them (a decision or request one of them made on another employee's day). Nothing was deleted.
            _db.ChangeTracker.Clear();
            return TestEmployeeDeleteOutcome.Refused(TestEmployeeDeleteStatus.Blocked, ["Something outside the file still refers to one of these employees (for example a correction or decision one of them made on someone else's day), so nothing was deleted."]);
        }
    }

    private async Task<(List<string> Numbers, List<string> Errors)> ReadNumbersAsync(Stream csv, CancellationToken cancellationToken)
    {
        string text;
        using (var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        {
            text = await reader.ReadToEndAsync(cancellationToken);
        }

        if (text.Length > MaxChars)
        {
            return ([], ["The file is too big (at most 2 million characters)."]);
        }

        var records = TestEmployeeCsv.Parse(text);
        if (records.Count == 0)
        {
            return ([], ["The file is empty: it needs a header row and one row for each employee."]);
        }

        var column = Array.FindIndex(records[0], name => NumberColumns.Contains(TestEmployeeCsv.Key(name)));
        if (column < 0)
        {
            return ([], ["The header row has no employee_number column."]);
        }

        var numbers = records.Skip(1)
            .Select(r => column < r.Length ? r[column] : string.Empty)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (numbers.Count == 0)
        {
            return ([], ["The file has no employee numbers."]);
        }

        return numbers.Count > MaxEmployees ? ([], [$"The file has {numbers.Count} employees; at most {MaxEmployees} can be deleted at once."]) : (numbers, []);
    }
}
