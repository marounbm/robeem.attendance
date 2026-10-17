using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceTransportService : IAttendanceTransportService
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;

    public AttendanceTransportService(IRobeemDbContext db, IAttendanceAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<TransportItem>> ListAsync(CancellationToken cancellationToken)
    {
        var definitions = await _db.AttendanceTransportDefinitions.AsNoTracking().ToListAsync(cancellationToken);
        var integrations = (await _db.AttendanceIntegrations.AsNoTracking().GroupBy(i => i.Transport).Select(g => new { Transport = g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            .ToDictionary(g => g.Transport.ToString(), g => g.Count, StringComparer.Ordinal);
        var providers = await _db.AttendanceProviderTransports.AsNoTracking()
            .GroupBy(t => t.TransportId)
            .Select(g => new { TransportId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TransportId, g => g.Count, cancellationToken);

        return definitions
            .OrderBy(t => Enum.TryParse<AttendanceTransport>(t.TransportKey, out var transport) ? (int)transport : int.MaxValue)
            .Select(t => new TransportItem
            {
                Id = t.Id,
                Key = t.TransportKey,
                DisplayName = t.DisplayName,
                IsPull = t.IsPull,
                UsesFileFormat = t.UsesFileFormat,
                IsActive = t.IsActive,
                IntegrationCount = integrations.GetValueOrDefault(t.TransportKey),
                ProviderCount = providers.GetValueOrDefault(t.Id),
            })
            .ToList();
    }

    public async Task<TransportInput?> GetInputAsync(int id, CancellationToken cancellationToken) =>
        await _db.AttendanceTransportDefinitions.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TransportInput { DisplayName = t.DisplayName, IsActive = t.IsActive })
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<SaveResult> UpdateAsync(int id, TransportInput input, CancellationToken cancellationToken)
    {
        var transport = await _db.AttendanceTransportDefinitions.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (transport is null)
        {
            return SaveResult.Missing();
        }

        var name = (input.DisplayName ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > 255)
        {
            return SaveResult.Invalid(nameof(TransportInput.DisplayName), "A name is required (at most 255 characters).");
        }

        // File upload is how every provider can be given files by hand; switching it off would leave nothing to upload by.
        if (!input.IsActive && transport.TransportKey == nameof(AttendanceTransport.FileUpload))
        {
            return SaveResult.Invalid(nameof(TransportInput.IsActive), "File upload can't be switched off: every provider can be given files by hand.");
        }

        var before = $"{transport.DisplayName}; {(transport.IsActive ? "active" : "inactive")}";
        transport.DisplayName = name;
        transport.IsActive = input.IsActive;
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceTransportDefinition), transport.Id, $"Transport {transport.TransportKey} changed. Before: {before}. After: {name}; {(transport.IsActive ? "active" : "inactive")}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(transport.Id);
    }
}
