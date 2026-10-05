using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class PayPeriodService : IPayPeriodService
{
    private const int MaxDays = 366;
    private const int ReasonMaxLength = 1000;

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;

    public PayPeriodService(IRobeemDbContext db, IAttendanceAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PayPeriodItem>> ListAsync(CancellationToken cancellationToken)
    {
        var periods = await _db.PayPeriods.AsNoTracking().OrderByDescending(p => p.StartDate).ThenByDescending(p => p.Id).ToListAsync(cancellationToken);
        return periods.Select(p => new PayPeriodItem { Id = p.Id, StartDate = p.StartDate.Date, EndDate = p.EndDate.Date, IsClosed = p.IsClosed }).ToList();
    }

    public async Task<SaveResult> CreateAsync(PayPeriodInput input, CancellationToken cancellationToken)
    {
        var start = input.StartDate.Date;
        var end = input.EndDate.Date;
        if (end < start)
        {
            return SaveResult.Invalid(nameof(PayPeriodInput.EndDate), "The period ends before it starts.");
        }

        if ((end - start).TotalDays + 1 > MaxDays)
        {
            return SaveResult.Invalid(nameof(PayPeriodInput.EndDate), $"A pay period can be at most {MaxDays} days.");
        }

        var overlap = await _db.PayPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.StartDate <= end && p.EndDate >= start, cancellationToken);
        if (overlap is not null)
        {
            return SaveResult.Invalid(nameof(PayPeriodInput.StartDate), $"The period overlaps another one ({overlap.StartDate:yyyy-MM-dd} to {overlap.EndDate:yyyy-MM-dd}).");
        }

        var period = new PayPeriod { StartDate = start, EndDate = end };
        _db.PayPeriods.Add(period);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.PayPeriodChanged, nameof(PayPeriod), period.Id, $"Created {start:yyyy-MM-dd} to {end:yyyy-MM-dd}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(period.Id);
    }

    public async Task<SaveResult> CloseAsync(int id, CancellationToken cancellationToken)
    {
        var period = await _db.PayPeriods.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (period is null)
        {
            return SaveResult.Missing();
        }

        if (period.IsClosed)
        {
            return SaveResult.Invalid("id", "The pay period is already closed.");
        }

        period.IsClosed = true;
        _audit.Record(AttendanceAuditActions.PayPeriodChanged, nameof(PayPeriod), period.Id, $"Closed {period.StartDate:yyyy-MM-dd} to {period.EndDate:yyyy-MM-dd}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(id);
    }

    public async Task<SaveResult> ReopenAsync(int id, string? reason, CancellationToken cancellationToken)
    {
        var period = await _db.PayPeriods.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (period is null)
        {
            return SaveResult.Missing();
        }

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed is null)
        {
            return SaveResult.Invalid(nameof(ReopenPayPeriodRequest.Reason), "A reason is required to reopen a closed pay period.");
        }

        if (trimmed.Length > ReasonMaxLength)
        {
            return SaveResult.Invalid(nameof(ReopenPayPeriodRequest.Reason), $"The reason can be at most {ReasonMaxLength} characters.");
        }

        if (!period.IsClosed)
        {
            return SaveResult.Invalid("id", "The pay period is not closed.");
        }

        period.IsClosed = false;
        _audit.Record(AttendanceAuditActions.PayPeriodChanged, nameof(PayPeriod), period.Id, $"Reopened {period.StartDate:yyyy-MM-dd} to {period.EndDate:yyyy-MM-dd}. Reason: {trimmed}");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(id);
    }
}
