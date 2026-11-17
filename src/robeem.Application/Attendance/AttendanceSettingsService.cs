using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceSettingsService : IAttendanceSettingsService
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;

    public AttendanceSettingsService(IRobeemDbContext db, IAttendanceAuditTrail audit, TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<AttendanceSettingsItem> GetAsync(CancellationToken cancellationToken)
    {
        var start = await AttendanceStartDate.ReadAsync(_db, cancellationToken);

        return new AttendanceSettingsItem { StartDate = start, LastAttendanceDate = await AttendanceStartDate.ReadLastAttendanceDateAsync(_db, cancellationToken) };
    }

    public async Task<SaveResult> SaveAsync(AttendanceSettingsInput input, CancellationToken cancellationToken)
    {
        var start = input.StartDate?.Date;
        if (start is { } s && s > _clock.GetLocalNow().DateTime.Date)
        {
            return SaveResult.Invalid(nameof(AttendanceSettingsInput.StartDate), "The punching can't have started in the future.");
        }

        var settings = await _db.AttendanceSettings.SingleOrDefaultAsync(cancellationToken);
        var before = settings?.StartDate?.Date;
        if (before == start)
        {
            return SaveResult.Success(settings?.Id ?? 0);
        }

        if (settings is null)
        {
            settings = new AttendanceSettings();
            _db.AttendanceSettings.Add(settings);
        }

        settings.StartDate = start is null ? null : DateTime.SpecifyKind(start.Value, DateTimeKind.Unspecified);

        // Moving the date earlier (or clearing it) lets punches that were kept out for being too early be read now: they go back to
        // Pending, and normalization fails again any that are still before the date. Only status and error of a raw row change (invariant 2).
        var requeued = 0;
        if (before is not null && (start is null || start < before))
        {
            var tooEarly = await _db.RawAttendanceTransactions
                .Where(r => r.ProcessingStatus == RawTransactionProcessingStatus.Failed
                    && r.ProcessingError != null
                    && r.ProcessingError.StartsWith(AttendanceStartDate.BeforeStartPrefix))
                .ToListAsync(cancellationToken);
            foreach (var raw in tooEarly)
            {
                raw.ProcessingStatus = RawTransactionProcessingStatus.Pending;
                raw.ProcessingError = null;
            }

            requeued = tooEarly.Count;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var details = $"The start date was {(before is null ? "not set" : $"{before:yyyy-MM-dd}")} and is now {(start is null ? "not set" : $"{start:yyyy-MM-dd}")}."
            + (requeued > 0 ? $" {requeued} punch(es) that were before the old date were queued to be normalized again." : string.Empty);
        _audit.Record(AttendanceAuditActions.AttendanceSettingsChanged, nameof(AttendanceSettings), settings.Id, details);
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(settings.Id);
    }
}
