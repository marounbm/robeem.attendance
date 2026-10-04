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

        var run = new Run();
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

        // a. Employee, through the integration's id mapping.
        int? employeeId = raw.ExternalEmployeeId is { } externalEmployeeId && integration.Employees.TryGetValue(externalEmployeeId, out var mapped)
            ? mapped
            : null;

        // b. Device: resolved at ingestion when it was already known, otherwise by its external id.
        var device = raw.DeviceId is { } knownDeviceId
            ? integration.Devices.Values.FirstOrDefault(d => d.Id == knownDeviceId)
            : raw.DeviceExternalId is { } externalDeviceId && integration.Devices.TryGetValue(externalDeviceId, out var found) ? found : null;

        // e. Timestamp: a structurally unusable one fails the raw row; no normalized row is made.
        var timestamp = integration.Interpreter.ParseTimestamp(raw.TransactionTimestampRaw);
        if (timestamp is null)
        {
            await FailAsync(raw, $"Unparseable timestamp '{raw.TransactionTimestampRaw}'.", run, cancellationToken);
            return;
        }

        var utc = raw.TransactionTimestampParsed ?? timestamp.Value.ToUtc(device?.TimeZoneId);
        if (utc is null)
        {
            var reason = device is null
                ? $"The timestamp has no UTC offset and device '{raw.DeviceExternalId}' is unknown, so its time zone is too. Register the device, then reprocess."
                : $"The timestamp does not exist in device time zone '{device.TimeZoneId}' (daylight-saving gap).";
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
            await FailAsync(raw, $"Unrecognized event type '{OriginalEventType(raw)}' for provider '{integration.ProviderKey}'.", run, cancellationToken);
            return;
        }

        // Local time in the device's zone; UTC when the device (and so its zone) is unknown. Of
        // unspecified kind: local columns are `timestamp without time zone`, which refuse a UTC kind.
        var timeZone = device is not null && TimeZoneInfo.TryFindSystemTimeZoneById(device.TimeZoneId, out var deviceZone) ? deviceZone : null;
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
            TimeZoneId = timeZone is null ? UtcTimeZoneId : device!.TimeZoneId,
            NormalizedEventType = eventType,
            OriginalEventType = OriginalEventType(raw),
            EventSource = AttendanceEventSource.Device,
            ConfidenceStatus = confidence,
            ExceptionStatus = employeeId is null ? TransactionExceptionStatus.FlaggedUnrecognizedEmployee
                : device is null ? TransactionExceptionStatus.FlaggedUnrecognizedDevice
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

        var providerKey = await _db.AttendanceIntegrations
            .Where(i => i.Id == integrationId)
            .Select(i => i.Provider!.ProviderKey)
            .SingleAsync(cancellationToken);
        var interpreter = _services.GetKeyedService<IAttendanceEventInterpreter>(providerKey) ?? throw new MissingInterpreterException(providerKey);

        var devices = await _db.AttendanceDevices
            .AsNoTracking()
            .Where(d => d.IntegrationId == integrationId)
            .ToDictionaryAsync(d => d.ExternalDeviceId, StringComparer.Ordinal, cancellationToken);
        var employees = await _db.ExternalEmployeeIdMappings
            .Where(m => m.IntegrationId == integrationId)
            .ToDictionaryAsync(m => m.ExternalEmployeeId, m => m.EmployeeId, StringComparer.Ordinal, cancellationToken);

        return run.Integrations[integrationId] = new IntegrationLookups(providerKey, interpreter, devices, employees);
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
        string ProviderKey,
        IAttendanceEventInterpreter Interpreter,
        IReadOnlyDictionary<string, AttendanceDevice> Devices,
        IReadOnlyDictionary<string, int> Employees);

    /// <summary>Per-run state: counters and lookups loaded once per batch.</summary>
    private sealed class Run
    {
        public Dictionary<int, IntegrationLookups> Integrations { get; } = [];

        public Dictionary<int, AttendancePolicy> Policies { get; } = [];

        public int Normalized { get; set; }

        public int Duplicates { get; set; }

        public int Flagged { get; set; }

        public int Failed { get; set; }
    }

    private sealed class MissingInterpreterException(string providerKey)
        : InvalidOperationException($"No attendance event interpreter is registered for provider '{providerKey}'.");
}
