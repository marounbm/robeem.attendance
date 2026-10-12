using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Hands each employee day with new normalized punches to <see cref="IAttendanceProcessingEngine"/>.
/// The engine is safe to repeat, so a day that is processed twice (a late punch for a day that was
/// already calculated) changes nothing the second time unless the result changed.
/// </summary>
public sealed class PendingDayProcessor : IPendingDayProcessor
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendanceProcessingEngine _engine;
    private readonly ILogger<PendingDayProcessor> _logger;

    public PendingDayProcessor(IRobeemDbContext db, IAttendanceProcessingEngine engine, ILogger<PendingDayProcessor> logger)
    {
        _db = db;
        _engine = engine;
        _logger = logger;
    }

    public async Task<int> ProcessPendingAsync(int maxDays, CancellationToken cancellationToken)
    {
        var pending = await _db.NormalizedAttendanceTransactions
            .AsNoTracking()
            .Where(n => n.ProcessingStatus == NormalizedTransactionProcessingStatus.Pending && n.EmployeeId != null)
            .Select(n => new { EmployeeId = n.EmployeeId!.Value, n.AttendanceDate })
            .Distinct()
            .OrderBy(d => d.AttendanceDate)
            .ThenBy(d => d.EmployeeId)
            .Take(Math.Max(1, maxDays))
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var day in pending)
        {
            try
            {
                await ProcessDayAsync(day.EmployeeId, day.AttendanceDate.Date, cancellationToken);
                processed++;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The day stays Pending and is tried again on the next run; the others go on.
                _logger.LogError(ex, "Processing attendance of employee {EmployeeId} for {Date:yyyy-MM-dd} failed.", day.EmployeeId, day.AttendanceDate);
                _db.ChangeTracker.Clear();
            }
        }

        return processed;
    }

    private async Task ProcessDayAsync(int employeeId, DateTime day, CancellationToken cancellationToken)
    {
        await _engine.ProcessEmployeeDayAsync(employeeId, day, cancellationToken);

        // A late punch for the day after an overnight shift changes that shift's record, which is
        // dated on the day before; recalculate it too, but only if the employee has punches there,
        // so a day nobody has data for gets no no-show exception.
        var previous = day.AddDays(-1);
        if (await _db.NormalizedAttendanceTransactions.AnyAsync(n => n.EmployeeId == employeeId && n.AttendanceDate == previous, cancellationToken))
        {
            await _engine.ProcessEmployeeDayAsync(employeeId, previous, cancellationToken);
        }

        var rows = await _db.NormalizedAttendanceTransactions
            .Where(n => n.EmployeeId == employeeId && n.AttendanceDate == day && n.ProcessingStatus == NormalizedTransactionProcessingStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.ProcessingStatus = NormalizedTransactionProcessingStatus.Processed;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
