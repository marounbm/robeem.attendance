using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceConnectorService : IAttendanceConnectorService
{
    private static readonly Regex KeyPattern = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant);

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;
    private readonly IAttendanceAdapterCatalog _adapters;
    private readonly IPayloadDecryptorCatalog? _decryptors;

    public AttendanceConnectorService(IRobeemDbContext db, IAttendanceAuditTrail audit, IAttendanceAdapterCatalog adapters, IPayloadDecryptorCatalog? decryptors = null)
    {
        _db = db;
        _audit = audit;
        _adapters = adapters;
        _decryptors = decryptors;
    }

    public async Task<IReadOnlyList<ConnectorListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var connectors = await _db.AttendanceConnectorDefinitions.AsNoTracking()
            .Include(c => c.Vendor)
            .Include(c => c.Transports).ThenInclude(t => t.Transport)
            .Include(c => c.DefaultPayloadFormat)
            .OrderBy(c => c.Vendor!.DisplayName).ThenBy(c => c.DisplayName)
            .ToListAsync(cancellationToken);
        var counts = await _db.AttendanceIntegrations.AsNoTracking()
            .GroupBy(i => i.ConnectorId)
            .Select(g => new { ConnectorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ConnectorId, g => g.Count, cancellationToken);
        var madeFor = (await _db.AttendancePayloadFormats.AsNoTracking()
                .Where(f => f.ConnectorId != null)
                .Select(f => new { ConnectorId = f.ConnectorId!.Value, f.PayloadKind })
                .ToListAsync(cancellationToken))
            .ToLookup(f => f.ConnectorId, f => f.PayloadKind.ToString());

        return connectors.Select(c => new ConnectorListItem
        {
            Id = c.Id,
            VendorId = c.VendorId,
            VendorName = c.Vendor!.DisplayName,
            ConnectorKey = c.ConnectorKey,
            DisplayName = c.DisplayName,
            Description = c.Description,
            AdapterKey = c.AdapterKey,
            Transports = OwnTransportKeys(c),
            DefaultPayloadFormatId = c.DefaultPayloadFormatId,
            DefaultPayloadFormatName = c.DefaultPayloadFormat?.Name,
            PayloadKinds = madeFor[c.Id].Concat(c.DefaultPayloadFormat is null ? [] : [c.DefaultPayloadFormat.PayloadKind.ToString()]).Distinct().Order().ToList(),
            SupportsIncrementalSync = c.SupportsIncrementalSync,
            EncryptionKind = c.EncryptionKind.ToString(),
            IsActive = c.IsActive,
            HasAdapter = c.AdapterKey is { Length: > 0 } key && _adapters.IsRegistered(key),
            IsUsable = ConnectorUsability.IsUsable(c, c.Vendor.IsActive, _adapters, _decryptors),
            IntegrationCount = counts.GetValueOrDefault(c.Id),
        }).ToList();
    }

    public async Task<ConnectorInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        var connector = await _db.AttendanceConnectorDefinitions.AsNoTracking()
            .Include(c => c.Transports).ThenInclude(t => t.Transport)
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        return connector is null
            ? null
            : new ConnectorInput
            {
                VendorId = connector.VendorId,
                ConnectorKey = connector.ConnectorKey,
                DisplayName = connector.DisplayName,
                Description = connector.Description,
                AdapterKey = connector.AdapterKey,
                Transports = OwnTransportKeys(connector).ToList(),
                DefaultPayloadFormatId = connector.DefaultPayloadFormatId,
                SupportsIncrementalSync = connector.SupportsIncrementalSync,
                EncryptionKind = connector.EncryptionKind.ToString(),
                IsActive = connector.IsActive,
            };
    }

    public async Task<SaveResult> CreateAsync(ConnectorInput input, CancellationToken cancellationToken)
    {
        var errors = await ValidateAsync(input, null, null, new HashSet<int>(), cancellationToken);
        var key = Clean(input.ConnectorKey);
        if (key.Length > 0 && !errors.ContainsKey(nameof(ConnectorInput.ConnectorKey))
            && await _db.AttendanceConnectorDefinitions.AnyAsync(c => c.ConnectorKey == key, cancellationToken))
        {
            errors[nameof(ConnectorInput.ConnectorKey)] = "Another connector already has this key.";
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var connector = new AttendanceConnectorDefinition { ConnectorKey = key, VendorId = input.VendorId };
        await ApplyAsync(input, connector, cancellationToken);
        _db.AttendanceConnectorDefinitions.Add(connector);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceConnectorDefinition), connector.Id, $"Connector '{connector.DisplayName}' ({connector.ConnectorKey}) created: {Summary(input)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(connector.Id);
    }

    public async Task<SaveResult> UpdateAsync(int id, ConnectorInput input, CancellationToken cancellationToken)
    {
        var connector = await _db.AttendanceConnectorDefinitions.Include(c => c.Transports).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connector is null)
        {
            return SaveResult.Missing();
        }

        var errors = await ValidateAsync(input, connector, connector.DefaultPayloadFormatId, connector.Transports.Select(t => t.TransportId).ToHashSet(), cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = $"{connector.DisplayName}; {(connector.IsActive ? "active" : "inactive")}; format {connector.DefaultPayloadFormatId?.ToString() ?? "none"}; adapter '{connector.AdapterKey}'";
        await ApplyAsync(input, connector, cancellationToken);
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceConnectorDefinition), connector.Id, $"Connector '{connector.ConnectorKey}' changed. Before: {before}. After: {Summary(input)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(connector.Id);
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    private static string Summary(ConnectorInput input) =>
        $"{Clean(input.DisplayName)}; {(input.IsActive ? "active" : "inactive")}; format {input.DefaultPayloadFormatId?.ToString() ?? "none"}; adapter '{Clean(input.AdapterKey)}'; transports {string.Join(",", input.Transports)}";

    private static IReadOnlyList<string> OwnTransportKeys(AttendanceConnectorDefinition connector) =>
        connector.Transports.Where(t => t.Transport is not null).Select(t => t.Transport!.TransportKey).OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>The transports asked for, without file upload (which every connector has) and without repeats.</summary>
    private static List<string> WantedTransports(ConnectorInput input) =>
        input.Transports
            .Select(Clean)
            .Where(t => t.Length > 0 && !string.Equals(t, nameof(AttendanceTransport.FileUpload), StringComparison.OrdinalIgnoreCase))
            .Select(t => Enum.TryParse<AttendanceTransport>(t, ignoreCase: true, out var transport) && Enum.IsDefined(transport) ? transport.ToString() : t)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private async Task ApplyAsync(ConnectorInput input, AttendanceConnectorDefinition connector, CancellationToken cancellationToken)
    {
        connector.DisplayName = Clean(input.DisplayName);
        connector.Description = Clean(input.Description);
        connector.AdapterKey = Clean(input.AdapterKey) is { Length: > 0 } adapterKey ? adapterKey : null;
        connector.DefaultPayloadFormatId = input.DefaultPayloadFormatId;
        connector.SupportsIncrementalSync = input.SupportsIncrementalSync;
        connector.EncryptionKind = Enum.Parse<PayloadEncryptionKind>(Clean(input.EncryptionKind).Length == 0 ? nameof(PayloadEncryptionKind.None) : Clean(input.EncryptionKind), ignoreCase: true);
        connector.IsActive = input.IsActive;

        var wanted = WantedTransports(input);
        var definitions = await _db.AttendanceTransportDefinitions.Where(t => wanted.Contains(t.TransportKey)).ToListAsync(cancellationToken);
        var keep = definitions.Select(d => d.Id).ToHashSet();
        foreach (var existing in connector.Transports.Where(t => !keep.Contains(t.TransportId)).ToList())
        {
            _db.AttendanceConnectorTransports.Remove(existing);
        }

        var present = connector.Transports.Select(t => t.TransportId).ToHashSet();
        foreach (var definition in definitions.Where(d => !present.Contains(d.Id)))
        {
            connector.Transports.Add(new AttendanceConnectorTransport { TransportId = definition.Id });
        }
    }

    private async Task<Dictionary<string, string>> ValidateAsync(
        ConnectorInput input,
        AttendanceConnectorDefinition? existing,
        int? currentFormatId,
        IReadOnlySet<int> currentTransportIds,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();

        var key = Clean(input.ConnectorKey);
        if (existing is not null)
        {
            if (key != existing.ConnectorKey)
            {
                errors[nameof(ConnectorInput.ConnectorKey)] = "The key can't be changed: integrations are tied to it.";
            }

            if (input.VendorId != existing.VendorId)
            {
                errors[nameof(ConnectorInput.VendorId)] = "The vendor can't be changed: the punches of its integrations are tied to it.";
            }
        }
        else
        {
            if (key.Length == 0 || key.Length > 100 || !KeyPattern.IsMatch(key))
            {
                errors[nameof(ConnectorInput.ConnectorKey)] = "A key is required: lower-case letters, digits, '.', '_' and '-', at most 100 characters.";
            }

            if (!await _db.AttendanceVendors.AnyAsync(v => v.Id == input.VendorId, cancellationToken))
            {
                errors[nameof(ConnectorInput.VendorId)] = "The selected vendor does not exist.";
            }
        }

        var name = Clean(input.DisplayName);
        if (name.Length == 0 || name.Length > 255)
        {
            errors[nameof(ConnectorInput.DisplayName)] = "A name is required (at most 255 characters).";
        }

        if (Clean(input.Description).Length > 1000)
        {
            errors[nameof(ConnectorInput.Description)] = "The description can be at most 1000 characters.";
        }

        var adapterKey = Clean(input.AdapterKey);
        if (adapterKey.Length > 0 && (adapterKey.Length > 100 || !KeyPattern.IsMatch(adapterKey)))
        {
            errors[nameof(ConnectorInput.AdapterKey)] = "An adapter key is lower-case letters, digits, '.', '_' and '-', at most 100 characters; leave it empty when no code reads this connector.";
        }

        var encryption = Clean(input.EncryptionKind).Length == 0 ? nameof(PayloadEncryptionKind.None) : Clean(input.EncryptionKind);
        if (!Enum.TryParse<PayloadEncryptionKind>(encryption, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
        {
            errors[nameof(ConnectorInput.EncryptionKind)] = $"The encryption must be one of: {string.Join(", ", Enum.GetNames<PayloadEncryptionKind>())}.";
        }
        else if (!(_decryptors?.CanRead(kind) ?? kind == PayloadEncryptionKind.None))
        {
            errors[nameof(ConnectorInput.EncryptionKind)] = $"This server has no way to decrypt a {kind} payload.";
        }

        var wanted = WantedTransports(input);
        var unknown = wanted.FirstOrDefault(t => !Enum.TryParse<AttendanceTransport>(t, out var transport) || !Enum.IsDefined(transport));
        if (unknown is not null)
        {
            errors[nameof(ConnectorInput.Transports)] = $"'{unknown}' is not a transport. Choose from: {string.Join(", ", Enum.GetNames<AttendanceTransport>())}.";
        }
        else if (wanted.Count > 0)
        {
            var definitions = await _db.AttendanceTransportDefinitions.AsNoTracking().Where(t => wanted.Contains(t.TransportKey)).ToListAsync(cancellationToken);
            if (definitions.Count != wanted.Count)
            {
                errors[nameof(ConnectorInput.Transports)] = "A transport you chose has no definition in this tenant.";
            }
            else if (definitions.FirstOrDefault(t => !t.IsActive && !currentTransportIds.Contains(t.Id)) is { } retired)
            {
                // Like a retired payload format: a connector may keep a transport that has since been switched off, but can't be given it.
                errors[nameof(ConnectorInput.Transports)] = $"{retired.DisplayName} is switched off, so it can't be added to a connector.";
            }
        }

        // A connector may keep a format that has since been retired; a new choice must be an active one.
        if (input.DefaultPayloadFormatId is { } formatId
            && !await _db.AttendancePayloadFormats.AnyAsync(f => f.Id == formatId && (f.IsActive || f.Id == currentFormatId) && !_db.AttendancePayloadFormats.Any(newer => newer.SupersedesId == f.Id), cancellationToken))
        {
            errors[nameof(ConnectorInput.DefaultPayloadFormatId)] = "The selected payload format does not exist or is not active.";
        }

        return errors;
    }
}
