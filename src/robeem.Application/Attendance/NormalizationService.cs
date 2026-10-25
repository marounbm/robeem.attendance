using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class NormalizationService : INormalizationService
{
    private const string UtcTimeZoneId = "UTC";

    private readonly IRobeemDbContext _db;
    private readonly INormalizedTransactionRecorder _recorder;
    private readonly IAttendancePolicyResolver _policies;
    private readonly IServiceProvider _services;
    private readonly ILogger<NormalizationService> _logger;

    public NormalizationService(
        IRobeemDbContext db,
        INormalizedTransactionRecorder recorder,
        IAttendancePolicyResolver policies,
        IServiceProvider services,
        ILogger<NormalizationService> logger)
    {
        _db = db;
        _recorder = recorder;
        _policies = policies;
        _services = services;
        _logger = logger;
    }

    public async Task<IReadOnlyList<int>> ListBatchesWithPendingTransactionsAsync(CancellationToken cancellationToken) =>
        await _db.RawAttendanceTransactions
            .Where(r => r.ProcessingStatus == RawTransactionProcessingStatus.Pending)
            .Select(r => r.ImportBatchId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);

    public async Task<NormalizationResult> NormalizeBatchAsync(int batchId, CancellationToken cancellationToken)
    {
        var pendingIds = await _db.RawAttendanceTransactions
            .Where(r => r.ImportBatchId == batchId && r.ProcessingStatus == RawTransactionProcessingStatus.Pending)
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        // The payload format version the batch was read by, if it records one: its rows are interpreted by that version even
        // when the format has been changed since (CLAUDE.md, Stage 4 follow-up).
        var run = new Run
        {
            PayloadFormatId = await _db.AttendanceProcessingBatches.Where(b => b.Id == batchId).Select(b => b.PayloadFormatId).SingleOrDefaultAsync(cancellationToken),
        };
        foreach (var rawId in pendingIds)
        {
            try
            {
                var raw = await _db.RawAttendanceTransactions.SingleAsync(r => r.Id == rawId, cancellationToken);
                await NormalizeAsync(raw, run, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not MissingInterpreterException)
            {
                // An unexpected error must not stall the batch, nor be retried forever: fail this
                // row with the reason. It can be set back to Pending to retry.
                _logger.LogError(ex, "Normalizing raw attendance transaction {RawId} failed.", rawId);
                _db.ChangeTracker.Clear();
                var raw = await _db.RawAttendanceTransactions.SingleAsync(r => r.Id == rawId, cancellationToken);
                await FailAsync(raw, $"Normalization error: {ex.GetBaseException().Message}", run, cancellationToken);
            }
        }

        return new NormalizationResult(run.Normalized, run.Duplicates, run.Flagged, run.Failed);
    }

    private static DateTimeOffset Bucket(DateTimeOffset utc, int windowSeconds)
    {
        var window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds)).Ticks;
        return new DateTimeOffset(utc.UtcTicks - (utc.UtcTicks % window), TimeSpan.Zero);
    }

    private static string? OriginalEventType(RawAttendanceTransaction raw)
    {
        var parts = new[] { raw.TransactionTypeRaw, raw.DirectionRaw }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private async Task NormalizeAsync(RawAttendanceTransaction raw, Run run, CancellationToken cancellationToken)
    {
        var integration = await GetIntegrationAsync(raw.IntegrationId, run, cancellationToken);

        // b. Device: an uploaded file has no physical device, so the integration's own device takes every
        // punch of it; otherwise resolved at ingestion when it was already known, else by its external id.
        var device = integration.DefaultDevice
            ?? (raw.DeviceId is { } knownDeviceId
                ? integration.Devices.Values.FirstOrDefault(d => d.Id == knownDeviceId)
                : raw.DeviceExternalId is { } externalDeviceId && integration.Devices.TryGetValue(externalDeviceId, out var found) ? found : null);

        // a. Employee, through the id mapping of the punch's device: the same vendor id can mean different
        // people on different devices, so a punch of an unrecognized device has no employee either.
        int? employeeId = device is not null
            && raw.ExternalEmployeeId is { } externalEmployeeId
            && integration.Employees.TryGetValue(device.Id, out var onDevice)
            && onDevice.TryGetValue(externalEmployeeId, out var mapped)
                ? mapped
                : null;

        // e. Timestamp: a structurally unusable one fails the raw row; no normalized row is made.
        var timestamp = integration.Interpreter.ParseTimestamp(raw.TransactionTimestampRaw);
        if (timestamp is null)
        {
            await FailAsync(raw, $"Unparseable timestamp '{raw.TransactionTimestampRaw}'.", run, cancellationToken);
            return;
        }

        // The zone a wall-clock time is read in: its device's, else the integration's.
        var zoneId = !string.IsNullOrWhiteSpace(device?.TimeZoneId) ? device!.TimeZoneId
            : !string.IsNullOrWhiteSpace(integration.TimeZoneId) ? integration.TimeZoneId
            : null;

        var utc = raw.TransactionTimestampParsed ?? timestamp.Value.ToUtc(zoneId);
        if (utc is null)
        {
            var reason = zoneId is null
                ? $"{NormalizationFailures.NoTimeZonePrefix} and device '{raw.DeviceExternalId}' is unknown, so its time zone is too, and the integration has no time zone. Set the integration's time zone (or register the device), then retry the failed punches."
                : $"The timestamp does not exist in {(device is null ? "integration" : "device")} time zone '{zoneId}' (daylight-saving gap).";
            await FailAsync(raw, reason, run, cancellationToken);
            return;
        }

        var policy = await GetPolicyAsync(employeeId, run, cancellationToken);

        // c. Event type, through the vendor's interpreter (CLAUDE.md §1.9).
        NormalizedEventType eventType;
        EventConfidenceStatus confidence;
        if (string.IsNullOrWhiteSpace(raw.TransactionTypeRaw) && string.IsNullOrWhiteSpace(raw.DirectionRaw))
        {
            // An unlabeled punch is kept for interpretation only when the policy allows it
            // (CLAUDE.md §1.6); processing then assigns the type and marks it Inferred.
            if (policy.UnlabeledPunchInterpretationMode == UnlabeledPunchInterpretationMode.Off)
            {
                await FailAsync(raw, "The punch has no event type or direction, and the attendance policy does not interpret unlabeled punches.", run, cancellationToken);
                return;
            }

            (eventType, confidence) = (NormalizedEventType.Unknown, EventConfidenceStatus.Ambiguous);
        }
        else if (integration.Interpreter.MapEventType(raw.TransactionTypeRaw, raw.DirectionRaw) is { } recognized)
        {
            (eventType, confidence) = (recognized, EventConfidenceStatus.Certain);
        }
        else
        {
            await FailAsync(raw, $"Unrecognized event type '{OriginalEventType(raw)}' for vendor '{integration.VendorKey}'.", run, cancellationToken);
            return;
        }

        // Local time in the device's zone, else the integration's; UTC when neither is known. Of
        // unspecified kind: local columns are `timestamp without time zone`, which refuse a UTC kind.
        var timeZone = zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var foundZone) ? foundZone : null;
        var local = DateTime.SpecifyKind(
            timeZone is null ? utc.Value.UtcDateTime : TimeZoneInfo.ConvertTime(utc.Value, timeZone).DateTime,
            DateTimeKind.Unspecified);

        // d. Unresolved employee or device: the row is still made, flagged.
        var normalized = new NormalizedAttendanceTransaction
        {
            EmployeeId = employeeId,
            DeviceId = device?.Id,
            IntegrationId = raw.IntegrationId,
            RawTransactionId = raw.Id,
            AttendanceDate = local.Date,
            EventTimestampUtc = utc.Value,
            EventTimestampLocal = local,
            TimeZoneId = timeZone is null ? UtcTimeZoneId : zoneId!,
            NormalizedEventType = eventType,
            OriginalEventType = OriginalEventType(raw),
            EventSource = AttendanceEventSource.Device,
            ConfidenceStatus = confidence,
            ExceptionStatus = device is null ? TransactionExceptionStatus.FlaggedUnrecognizedDevice
                : employeeId is null ? TransactionExceptionStatus.FlaggedUnrecognizedEmployee
                : TransactionExceptionStatus.None,

            // f. The duplicate key's time bucket.
            DuplicateBucketTimestampUtc = Bucket(utc.Value, policy.DuplicateTimeWindowSeconds),
        };

        // g. The raw row's status is saved together with the normalized row, by the recorder, which
        // also takes the duplicate decision (f).
        raw.ProcessingStatus = RawTransactionProcessingStatus.Normalized;
        raw.ProcessingError = null;
        await _recorder.RecordAsync(normalized, cancellationToken);

        run.Normalized++;
        run.Duplicates += normalized.DuplicateStatus == DuplicateStatus.DuplicateConfirmed ? 1 : 0;
        run.Flagged += normalized.ExceptionStatus == TransactionExceptionStatus.None ? 0 : 1;
    }

    private async Task FailAsync(RawAttendanceTransaction raw, string error, Run run, CancellationToken cancellationToken)
    {
        raw.ProcessingStatus = RawTransactionProcessingStatus.Failed;
        raw.ProcessingError = error;
        await _db.SaveChangesAsync(cancellationToken);
        run.Failed++;
    }

    private async Task<IntegrationLookups> GetIntegrationAsync(int integrationId, Run run, CancellationToken cancellationToken)
    {
        if (run.Integrations.TryGetValue(integrationId, out var cached))
        {
            return cached;
        }

        var integration = await _db.AttendanceIntegrations
            .Where(i => i.Id == integrationId)
            .Select(i => new { i.Connector!.Vendor!.VendorKey, i.Connector.AdapterKey, i.PayloadFormatId, ConnectorPayloadFormatId = i.Connector.DefaultPayloadFormatId, i.TimeZoneId, DefaultTransport = i.Connections.Where(c => c.IsDefault).Select(c => (AttendanceTransport?)c.Transport).FirstOrDefault(), i.DefaultDeviceId })
            .SingleAsync(cancellationToken);
        var vendorKey = integration.VendorKey;

        // An integration with no connection yet is read as a manual upload, as it was before connections existed.
        var transport = integration.DefaultTransport ?? AttendanceTransport.FileUpload;

        // An integration that reads its uploads by a payload format (its own, else its connector's default) is interpreted by that format's own patterns and labels.
        IAttendanceEventInterpreter interpreter;
        if ((run.PayloadFormatId ?? PayloadFormatResolution.EffectiveFormatId(transport, integration.PayloadFormatId, integration.ConnectorPayloadFormatId)) is { } formatId)
        {
            var format = await _db.AttendancePayloadFormats.AsNoTracking().Include(f => f.EventCodes).SingleAsync(f => f.Id == formatId, cancellationToken);
            interpreter = _services.GetRequiredService<ICustomFormatInterpreterFactory>().Create(format);
        }
        else
        {
            interpreter = (integration.AdapterKey is { Length: > 0 } adapterKey ? _services.GetKeyedService<IAttendanceEventInterpreter>(adapterKey) : null)
                ?? throw new MissingInterpreterException(integration.AdapterKey ?? string.Empty);
        }

        var devices = await _db.AttendanceDevices
            .AsNoTracking()
            .Where(d => d.IntegrationId == integrationId)
            .ToDictionaryAsync(d => d.ExternalDeviceId, StringComparer.Ordinal, cancellationToken);
        var employees = (await _db.ExternalEmployeeIdMappings
                .Where(m => m.Device!.IntegrationId == integrationId)
                .Select(m => new { m.DeviceId, m.ExternalEmployeeId, m.EmployeeId })
                .ToListAsync(cancellationToken))
            .GroupBy(m => m.DeviceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, int>)g.ToDictionary(m => m.ExternalEmployeeId, m => m.EmployeeId, StringComparer.Ordinal));

        // Only a manual upload has a device of its own for the file; any other transport reports its devices itself.
        var defaultDevice = transport == AttendanceTransport.FileUpload && integration.DefaultDeviceId is { } defaultDeviceId
            ? devices.Values.FirstOrDefault(d => d.Id == defaultDeviceId)
            : null;

        return run.Integrations[integrationId] = new IntegrationLookups(vendorKey, interpreter, devices, employees, integration.TimeZoneId, defaultDevice);
    }

    private async Task<AttendancePolicy> GetPolicyAsync(int? employeeId, Run run, CancellationToken cancellationToken)
    {
        var key = employeeId ?? 0;
        if (!run.Policies.TryGetValue(key, out var policy))
        {
            policy = await _policies.ResolveAsync(employeeId, cancellationToken);
            run.Policies[key] = policy;
        }

        return policy;
    }

    private sealed record IntegrationLookups(
        string VendorKey,
        IAttendanceEventInterpreter Interpreter,
        IReadOnlyDictionary<string, AttendanceDevice> Devices,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, int>> Employees,
        string? TimeZoneId,
        AttendanceDevice? DefaultDevice);

    /// <summary>Per-run state: counters and lookups loaded once per batch.</summary>
    private sealed class Run
    {
        public int? PayloadFormatId { get; init; }

        public Dictionary<int, IntegrationLookups> Integrations { get; } = [];

        public Dictionary<int, AttendancePolicy> Policies { get; } = [];

        public int Normalized { get; set; }

        public int Duplicates { get; set; }

        public int Flagged { get; set; }

        public int Failed { get; set; }
    }

    private sealed class MissingInterpreterException(string adapterKey)
        : InvalidOperationException($"No attendance event interpreter is registered under adapter key '{adapterKey}'.");
}
