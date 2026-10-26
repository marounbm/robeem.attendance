using System.Globalization;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceTransactionQuery : IAttendanceTransactionQuery
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;

    public AttendanceTransactionQuery(IRobeemDbContext db, IAttendanceAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PagedResult<RawTransactionListItem>> ListRawAsync(RawTransactionFilter filter, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(filter.PageSize <= 0 ? DefaultPageSize : filter.PageSize, 1, MaxPageSize);
        var search = Normalize(filter.Search);
        var employee = Normalize(filter.Employee);

        // A direct read of raw attendance outside the engine: audited, and saved before the read,
        // so the read is on record even if it then fails.
        var details = $"Raw punches (integration {Describe(filter.IntegrationId)}, batch {Describe(filter.BatchId)}, status {Describe(filter.Status)}, "
            + $"from {Describe(filter.From)}, to {Describe(filter.To)}, search '{search}', employee '{employee}', page {Math.Max(filter.Page, 1)}, {pageSize} per page).";
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

        if (employee.Length > 0)
        {
            // The employee the list shows for a punch: its vendor id, mapped on the device that took it. The device is found as
            // normalization finds it: the integration's device for uploaded files, else the one resolved at ingestion, else the
            // registered one with the file's own device id.
            raws = raws.Where(r => r.ExternalEmployeeId != null && _db.ExternalEmployeeIdMappings.Any(m =>
                m.ExternalEmployeeId == r.ExternalEmployeeId
                && (m.Employee!.EmployeeNumber.ToLower().Contains(employee) || (m.Employee.FirstName + " " + m.Employee.LastName).ToLower().Contains(employee))
                && ((r.Integration!.DefaultDeviceId != null && !r.Integration.Connections.Any(c => c.IsDefault && c.Transport != AttendanceTransport.FileUpload))
                    ? r.Integration.DefaultDeviceId == m.DeviceId
                    : r.DeviceId != null
                        ? r.DeviceId == m.DeviceId
                        : m.Device!.IntegrationId == r.IntegrationId && m.Device.ExternalDeviceId == r.DeviceExternalId)));
        }

        // The total is counted with the same filters as the page. A page past the end (a filter that narrowed the list since the
        // link was made) is answered with the last page.
        var total = await raws.CountAsync(cancellationToken);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));

        // Newest first by id, which is unique, so pages never overlap.
        var rows = await raws
            .OrderByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.IntegrationId,
                IntegrationName = r.Integration != null ? r.Integration.DisplayName : string.Empty,
                r.ImportBatchId,
                ReceivedAt = r.ImportBatch != null ? r.ImportBatch.StartedAt : default,
                r.ExternalTransactionId,
                r.ExternalEmployeeId,
                r.DeviceId,
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

        var employees = await MapEmployeesAsync(rows.Select(r => new RawKey(r.Id, r.IntegrationId, r.DeviceId, r.DeviceExternalId, r.ExternalEmployeeId)).ToList(), cancellationToken);

        var items = rows.Select(r => new RawTransactionListItem
        {
            Id = r.Id,
            IntegrationId = r.IntegrationId,
            IntegrationName = r.IntegrationName,
            BatchId = r.ImportBatchId,
            ReceivedAt = r.ReceivedAt,
            ExternalTransactionId = r.ExternalTransactionId,
            ExternalEmployeeId = r.ExternalEmployeeId,
            EmployeeId = employees.GetValueOrDefault(r.Id)?.Id,
            EmployeeNumber = employees.GetValueOrDefault(r.Id)?.Number,
            EmployeeName = employees.GetValueOrDefault(r.Id)?.Name,
            DeviceExternalId = r.DeviceExternalId,
            TimestampRaw = r.TransactionTimestampRaw,
            TypeRaw = r.TransactionTypeRaw,
            DirectionRaw = r.DirectionRaw,
            Status = r.ProcessingStatus.ToString(),
            ProcessingError = r.ProcessingError,
            Payload = r.RawPayload,
            NormalizedId = normalizedIds.TryGetValue(r.Id, out var normalizedId) ? normalizedId : null,
        }).ToList();

        return new PagedResult<RawTransactionListItem> { Items = items, Total = total, Page = page, PageSize = pageSize };
    }

    public async Task<PagedResult<NormalizedTransactionListItem>> ListNormalizedAsync(NormalizedTransactionFilter filter, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(filter.PageSize <= 0 ? DefaultPageSize : filter.PageSize, 1, MaxPageSize);
        var employee = Normalize(filter.Employee);

        var details = $"Normalized punches (integration {Describe(filter.IntegrationId)}, raw {Describe(filter.RawId)}, employee '{employee}', event {Describe(filter.EventType)}, "
            + $"duplicate {Describe(filter.Duplicate)}, flag {Describe(filter.Flag)}, from {Describe(filter.From)}, to {Describe(filter.To)}, page {Math.Max(filter.Page, 1)}, {pageSize} per page).";
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

        // The total is counted with the same filters as the page; a page past the end is answered with the last page.
        var total = await punches.CountAsync(cancellationToken);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));

        // Newest punch first, the id breaking ties so pages never overlap. The local time is a DateTime; the UTC instant is a
        // DateTimeOffset, which SQLite cannot order by.
        var rows = await punches
            .OrderByDescending(n => n.EventTimestampLocal)
            .ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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

        var items = rows.Select(n => new NormalizedTransactionListItem
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

        return new PagedResult<NormalizedTransactionListItem> { Items = items, Total = total, Page = page, PageSize = pageSize };
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

    /// <summary>
    /// The employee each raw punch's vendor id is mapped to today, found the way normalization finds it: the device first (the device
    /// for the files of an upload, else the one the punch was resolved to at ingestion, else the one with the file's device id), then
    /// the mapping of that vendor id on that device. A punch whose device is unknown, or whose id is not mapped on it, has none.
    /// </summary>
    private async Task<Dictionary<int, MappedEmployee>> MapEmployeesAsync(IReadOnlyList<RawKey> keys, CancellationToken cancellationToken)
    {
        var withId = keys.Where(k => !string.IsNullOrWhiteSpace(k.ExternalEmployeeId)).ToList();
        if (withId.Count == 0)
        {
            return [];
        }

        var integrationIds = withId.Select(k => k.IntegrationId).Distinct().ToList();
        var devices = await _db.AttendanceDevices.AsNoTracking()
            .Where(d => integrationIds.Contains(d.IntegrationId))
            .Select(d => new { d.Id, d.IntegrationId, d.ExternalDeviceId })
            .ToListAsync(cancellationToken);
        var uploadDevices = await _db.AttendanceIntegrations.AsNoTracking()
            .Where(i => integrationIds.Contains(i.Id) && i.DefaultDeviceId != null && !i.Connections.Any(c => c.IsDefault && c.Transport != AttendanceTransport.FileUpload))
            .Select(i => new { i.Id, DeviceId = i.DefaultDeviceId!.Value })
            .ToDictionaryAsync(i => i.Id, i => i.DeviceId, cancellationToken);

        int? DeviceOf(RawKey key) =>
            uploadDevices.TryGetValue(key.IntegrationId, out var uploadDevice) ? uploadDevice
            : key.DeviceId is { } known ? known
            : key.DeviceExternalId is { } external ? devices.FirstOrDefault(d => d.IntegrationId == key.IntegrationId && d.ExternalDeviceId == external)?.Id
            : null;

        var resolved = withId.Select(k => (Key: k, DeviceId: DeviceOf(k))).Where(x => x.DeviceId is not null).ToList();
        if (resolved.Count == 0)
        {
            return [];
        }

        var deviceIds = resolved.Select(x => x.DeviceId!.Value).Distinct().ToList();
        var externalIds = resolved.Select(x => x.Key.ExternalEmployeeId!).Distinct().ToList();
        var mappings = (await _db.ExternalEmployeeIdMappings.AsNoTracking()
                .Where(m => deviceIds.Contains(m.DeviceId) && externalIds.Contains(m.ExternalEmployeeId))
                .Select(m => new { m.DeviceId, m.ExternalEmployeeId, m.EmployeeId, m.Employee!.EmployeeNumber, m.Employee.FirstName, m.Employee.LastName })
                .ToListAsync(cancellationToken))
            .ToDictionary(m => (m.DeviceId, m.ExternalEmployeeId), m => new MappedEmployee(m.EmployeeId, m.EmployeeNumber, $"{m.FirstName} {m.LastName}".Trim()));

        // The match is exact, as in normalization: a vendor id is case-sensitive.
        var result = new Dictionary<int, MappedEmployee>();
        foreach (var (key, deviceId) in resolved)
        {
            if (mappings.TryGetValue((deviceId!.Value, key.ExternalEmployeeId!), out var employee))
            {
                result[key.RawId] = employee;
            }
        }

        return result;
    }

    private sealed record RawKey(int RawId, int IntegrationId, int? DeviceId, string? DeviceExternalId, string? ExternalEmployeeId);

    private sealed record MappedEmployee(int Id, string Number, string Name);
}
