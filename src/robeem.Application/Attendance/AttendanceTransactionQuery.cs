using System.Globalization;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceTransactionQuery : IAttendanceTransactionQuery
{
    private const int DefaultTake = 100;
    private const int MaxTake = 500;

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;

    public AttendanceTransactionQuery(IRobeemDbContext db, IAttendanceAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<RawTransactionListItem>> ListRawAsync(RawTransactionFilter filter, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(filter.Take <= 0 ? DefaultTake : filter.Take, 1, MaxTake);
        var search = Normalize(filter.Search);

        // A direct read of raw attendance outside the engine: audited, and saved before the read,
        // so the read is on record even if it then fails.
        var details = $"Raw punches (integration {Describe(filter.IntegrationId)}, batch {Describe(filter.BatchId)}, status {Describe(filter.Status)}, "
            + $"from {Describe(filter.From)}, to {Describe(filter.To)}, search '{search}', first {take}).";
        _audit.Record(AttendanceAuditActions.AttendanceDataRead, nameof(RawAttendanceTransaction), null, details);
        await _db.SaveChangesAsync(cancellationToken);

        var raws = _db.RawAttendanceTransactions.AsNoTracking();
        if (filter.IntegrationId is { } integrationId)
        {
            raws = raws.Where(r => r.IntegrationId == integrationId);
        }

        if (filter.BatchId is { } batchId)
        {
            raws = raws.Where(r => r.ImportBatchId == batchId);
        }

        if (filter.Status is { } status)
        {
            raws = raws.Where(r => r.ProcessingStatus == status);
        }

        // The batch's start is a DateTime; the punch's own parsed time is a DateTimeOffset, which SQLite cannot compare.
        if (filter.From is { } from)
        {
            var first = from.Date;
            raws = raws.Where(r => r.ImportBatch!.StartedAt >= first);
        }

        if (filter.To is { } to)
        {
            var end = to.Date.AddDays(1);
            raws = raws.Where(r => r.ImportBatch!.StartedAt < end);
        }

        if (search.Length > 0)
        {
            raws = raws.Where(r =>
                (r.ExternalEmployeeId != null && r.ExternalEmployeeId.ToLower().Contains(search))
                || (r.DeviceExternalId != null && r.DeviceExternalId.ToLower().Contains(search))
                || (r.ExternalTransactionId != null && r.ExternalTransactionId.ToLower().Contains(search)));
        }

        var rows = await raws
            .OrderByDescending(r => r.Id)
            .Take(take)
            .Select(r => new
            {
                r.Id,
                r.IntegrationId,
                IntegrationName = r.Integration != null ? r.Integration.DisplayName : string.Empty,
                r.ImportBatchId,
                ReceivedAt = r.ImportBatch != null ? r.ImportBatch.StartedAt : default,
                r.ExternalTransactionId,
                r.ExternalEmployeeId,
                r.DeviceExternalId,
                r.TransactionTimestampRaw,
                r.TransactionTypeRaw,
                r.DirectionRaw,
                r.ProcessingStatus,
                r.ProcessingError,
                r.RawPayload,
            })
            .ToListAsync(cancellationToken);

        var rawIds = rows.Select(r => r.Id).ToList();
        var normalizedIds = new Dictionary<int, int>();
        if (rawIds.Count > 0)
        {
            var links = await _db.NormalizedAttendanceTransactions.AsNoTracking()
                .Where(n => rawIds.Contains(n.RawTransactionId) && n.ProcessingStatus != NormalizedTransactionProcessingStatus.Excluded)
                .OrderBy(n => n.Id)
                .Select(n => new { n.RawTransactionId, n.Id })
                .ToListAsync(cancellationToken);
            foreach (var link in links)
            {
                normalizedIds.TryAdd(link.RawTransactionId, link.Id);
            }
        }

        return rows.Select(r => new RawTransactionListItem
        {
            Id = r.Id,
            IntegrationId = r.IntegrationId,
            IntegrationName = r.IntegrationName,
            BatchId = r.ImportBatchId,
            ReceivedAt = r.ReceivedAt,
            ExternalTransactionId = r.ExternalTransactionId,
            ExternalEmployeeId = r.ExternalEmployeeId,
            DeviceExternalId = r.DeviceExternalId,
            TimestampRaw = r.TransactionTimestampRaw,
            TypeRaw = r.TransactionTypeRaw,
            DirectionRaw = r.DirectionRaw,
            Status = r.ProcessingStatus.ToString(),
            ProcessingError = r.ProcessingError,
            Payload = r.RawPayload,
            NormalizedId = normalizedIds.TryGetValue(r.Id, out var normalizedId) ? normalizedId : null,
        }).ToList();
    }

    public async Task<IReadOnlyList<NormalizedTransactionListItem>> ListNormalizedAsync(NormalizedTransactionFilter filter, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(filter.Take <= 0 ? DefaultTake : filter.Take, 1, MaxTake);
        var employee = Normalize(filter.Employee);

        var details = $"Normalized punches (integration {Describe(filter.IntegrationId)}, raw {Describe(filter.RawId)}, employee '{employee}', event {Describe(filter.EventType)}, "
            + $"duplicate {Describe(filter.Duplicate)}, flag {Describe(filter.Flag)}, from {Describe(filter.From)}, to {Describe(filter.To)}, first {take}).";
        _audit.Record(AttendanceAuditActions.AttendanceDataRead, nameof(NormalizedAttendanceTransaction), null, details);
        await _db.SaveChangesAsync(cancellationToken);

        var punches = _db.NormalizedAttendanceTransactions.AsNoTracking();
        if (filter.IntegrationId is { } integrationId)
        {
            punches = punches.Where(n => n.IntegrationId == integrationId);
        }

        if (filter.RawId is { } rawId)
        {
            punches = punches.Where(n => n.RawTransactionId == rawId);
        }

        if (employee.Length > 0)
        {
            punches = punches.Where(n => n.Employee != null
                && (n.Employee.EmployeeNumber.ToLower().Contains(employee)
                    || (n.Employee.FirstName + " " + n.Employee.LastName).ToLower().Contains(employee)));
        }

        if (filter.EventType is { } eventType)
        {
            punches = punches.Where(n => n.NormalizedEventType == eventType);
        }

        if (filter.Duplicate is { } duplicate)
        {
            punches = punches.Where(n => n.DuplicateStatus == duplicate);
        }

        if (filter.Flag is { } flag)
        {
            punches = punches.Where(n => n.ExceptionStatus == flag);
        }

        if (filter.From is { } from)
        {
            var first = from.Date;
            punches = punches.Where(n => n.AttendanceDate >= first);
        }

        if (filter.To is { } to)
        {
            var last = to.Date;
            punches = punches.Where(n => n.AttendanceDate <= last);
        }

        // Newest punch first. The local time is a DateTime; the UTC instant is a DateTimeOffset, which SQLite cannot order by.
        var rows = await punches
            .OrderByDescending(n => n.EventTimestampLocal)
            .ThenByDescending(n => n.Id)
            .Take(take)
            .Select(n => new
            {
                n.Id,
                n.RawTransactionId,
                n.IntegrationId,
                IntegrationName = n.Integration != null ? n.Integration.DisplayName : string.Empty,
                EmployeeNumber = n.Employee != null ? n.Employee.EmployeeNumber : null,
                FirstName = n.Employee != null ? n.Employee.FirstName : null,
                LastName = n.Employee != null ? n.Employee.LastName : null,
                DeviceName = n.Device != null ? n.Device.Title : null,
                n.AttendanceDate,
                n.EventTimestampLocal,
                n.TimeZoneId,
                n.NormalizedEventType,
                n.OriginalEventType,
                n.EventSource,
                n.ConfidenceStatus,
                n.ProcessingStatus,
                n.DuplicateStatus,
                n.DuplicateOfTransactionId,
                n.ExceptionStatus,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(n => new NormalizedTransactionListItem
        {
            Id = n.Id,
            RawId = n.RawTransactionId,
            IntegrationId = n.IntegrationId,
            IntegrationName = n.IntegrationName,
            EmployeeNumber = n.EmployeeNumber,
            EmployeeName = n.FirstName is null ? null : $"{n.FirstName} {n.LastName}",
            DeviceName = n.DeviceName,
            AttendanceDate = n.AttendanceDate,
            LocalTime = n.EventTimestampLocal,
            TimeZoneId = n.TimeZoneId,
            EventType = n.NormalizedEventType.ToString(),
            OriginalEventType = n.OriginalEventType,
            Source = n.EventSource.ToString(),
            Confidence = n.ConfidenceStatus.ToString(),
            ProcessingStatus = n.ProcessingStatus.ToString(),
            DuplicateStatus = n.DuplicateStatus.ToString(),
            DuplicateOfId = n.DuplicateOfTransactionId,
            ExceptionStatus = n.ExceptionStatus.ToString(),
        }).ToList();
    }

    public async Task<IReadOnlyList<TransactionIntegrationOption>> ListIntegrationsAsync(CancellationToken cancellationToken) =>
        await _db.AttendanceIntegrations.AsNoTracking()
            .OrderBy(i => i.DisplayName)
            .Select(i => new TransactionIntegrationOption { Id = i.Id, Name = i.DisplayName })
            .ToListAsync(cancellationToken);

    private static string Normalize(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant();

    private static string Describe(object? value) => value switch
    {
        null => "any",
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "any",
    };
}
