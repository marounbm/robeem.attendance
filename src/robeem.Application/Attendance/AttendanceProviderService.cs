using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceProviderService : IAttendanceProviderService
{
    private static readonly Regex KeyPattern = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant);

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;
    private readonly IAttendanceAdapterCatalog _adapters;

    public AttendanceProviderService(IRobeemDbContext db, IAttendanceAuditTrail audit, IAttendanceAdapterCatalog adapters)
    {
        _db = db;
        _audit = audit;
        _adapters = adapters;
    }

    public async Task<IReadOnlyList<ProviderListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var providers = await _db.AttendanceProviders.AsNoTracking()
            .Include(p => p.Transports).ThenInclude(t => t.Transport)
            .Include(p => p.DefaultFileFormat)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(cancellationToken);
        var counts = await _db.AttendanceIntegrations.AsNoTracking()
            .GroupBy(i => i.ProviderId)
            .Select(g => new { ProviderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ProviderId, g => g.Count, cancellationToken);

        return providers.Select(p => new ProviderListItem
        {
            Id = p.Id,
            ProviderKey = p.ProviderKey,
            DisplayName = p.DisplayName,
            Manufacturer = p.Manufacturer,
            WebsiteUrl = p.WebsiteUrl,
            Transports = OwnTransportKeys(p),
            DefaultFileFormatId = p.DefaultFileFormatId,
            DefaultFileFormatName = p.DefaultFileFormat?.Name,
            SupportsWebhooks = p.SupportsWebhooks,
            SupportsIncrementalSync = p.SupportsIncrementalSync,
            IsActive = p.IsActive,
            HasAdapter = _adapters.IsRegistered(p.ProviderKey),
            IsUsable = ProviderUsability.IsUsable(p, _adapters),
            IntegrationCount = counts.GetValueOrDefault(p.Id),
        }).ToList();
    }

    public async Task<ProviderInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        var provider = await _db.AttendanceProviders.AsNoTracking()
            .Include(p => p.Transports).ThenInclude(t => t.Transport)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return provider is null
            ? null
            : new ProviderInput
            {
                ProviderKey = provider.ProviderKey,
                DisplayName = provider.DisplayName,
                Manufacturer = provider.Manufacturer,
                WebsiteUrl = provider.WebsiteUrl,
                Transports = OwnTransportKeys(provider).ToList(),
                DefaultFileFormatId = provider.DefaultFileFormatId,
                SupportsWebhooks = provider.SupportsWebhooks,
                SupportsIncrementalSync = provider.SupportsIncrementalSync,
                IsActive = provider.IsActive,
            };
    }

    public async Task<SaveResult> CreateAsync(ProviderInput input, CancellationToken cancellationToken)
    {
        var errors = await ValidateAsync(input, null, null, new HashSet<int>(), cancellationToken);
        var key = Clean(input.ProviderKey);
        if (key.Length > 0 && !errors.ContainsKey(nameof(ProviderInput.ProviderKey))
            && await _db.AttendanceProviders.AnyAsync(p => p.ProviderKey == key, cancellationToken))
        {
            errors[nameof(ProviderInput.ProviderKey)] = "Another provider already has this key.";
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var provider = new AttendanceProvider { ProviderKey = key };
        await ApplyAsync(input, provider, cancellationToken);
        _db.AttendanceProviders.Add(provider);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceProvider), provider.Id, $"Provider '{provider.DisplayName}' ({provider.ProviderKey}) created: {Summary(input)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(provider.Id);
    }

    public async Task<SaveResult> UpdateAsync(int id, ProviderInput input, CancellationToken cancellationToken)
    {
        var provider = await _db.AttendanceProviders.Include(p => p.Transports).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (provider is null)
        {
            return SaveResult.Missing();
        }

        var errors = await ValidateAsync(input, provider.DefaultFileFormatId, provider.ProviderKey, provider.Transports.Select(t => t.TransportId).ToHashSet(), cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = $"{provider.DisplayName}; {(provider.IsActive ? "active" : "inactive")}; format {provider.DefaultFileFormatId?.ToString() ?? "none"}";
        await ApplyAsync(input, provider, cancellationToken);
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceProvider), provider.Id, $"Provider '{provider.ProviderKey}' changed. Before: {before}. After: {Summary(input)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(provider.Id);
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    private static string Summary(ProviderInput input) =>
        $"{Clean(input.DisplayName)}; {(input.IsActive ? "active" : "inactive")}; format {input.DefaultFileFormatId?.ToString() ?? "none"}; transports {string.Join(",", input.Transports)}";

    private static IReadOnlyList<string> OwnTransportKeys(AttendanceProvider provider) =>
        provider.Transports.Where(t => t.Transport is not null).Select(t => t.Transport!.TransportKey).OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>The transports asked for, without file upload (which every provider has) and without repeats.</summary>
    private static List<string> WantedTransports(ProviderInput input) =>
        input.Transports
            .Select(Clean)
            .Where(t => t.Length > 0 && !string.Equals(t, nameof(AttendanceTransport.FileUpload), StringComparison.OrdinalIgnoreCase))
            .Select(t => Enum.TryParse<AttendanceTransport>(t, ignoreCase: true, out var transport) && Enum.IsDefined(transport) ? transport.ToString() : t)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private async Task ApplyAsync(ProviderInput input, AttendanceProvider provider, CancellationToken cancellationToken)
    {
        provider.DisplayName = Clean(input.DisplayName);
        provider.Manufacturer = Clean(input.Manufacturer);
        provider.WebsiteUrl = Clean(input.WebsiteUrl);
        provider.DefaultFileFormatId = input.DefaultFileFormatId;
        provider.SupportsWebhooks = input.SupportsWebhooks;
        provider.SupportsIncrementalSync = input.SupportsIncrementalSync;
        provider.IsActive = input.IsActive;

        var wanted = WantedTransports(input);
        var definitions = await _db.AttendanceTransportDefinitions.Where(t => wanted.Contains(t.TransportKey)).ToListAsync(cancellationToken);
        var keep = definitions.Select(d => d.Id).ToHashSet();
        foreach (var existing in provider.Transports.Where(t => !keep.Contains(t.TransportId)).ToList())
        {
            _db.AttendanceProviderTransports.Remove(existing);
        }

        var present = provider.Transports.Select(t => t.TransportId).ToHashSet();
        foreach (var definition in definitions.Where(d => !present.Contains(d.Id)))
        {
            provider.Transports.Add(new AttendanceProviderTransport { TransportId = definition.Id });
        }
    }

    private async Task<Dictionary<string, string>> ValidateAsync(ProviderInput input, int? currentFormatId, string? existingKey, IReadOnlySet<int> currentTransportIds, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();

        var key = Clean(input.ProviderKey);
        if (existingKey is not null)
        {
            if (key != existingKey)
            {
                errors[nameof(ProviderInput.ProviderKey)] = "The key can't be changed: a code adapter and the punches it produced are tied to it.";
            }
        }
        else if (key.Length == 0 || key.Length > 100 || !KeyPattern.IsMatch(key))
        {
            errors[nameof(ProviderInput.ProviderKey)] = "A key is required: lower-case letters, digits, '.', '_' and '-', at most 100 characters.";
        }

        var name = Clean(input.DisplayName);
        if (name.Length == 0 || name.Length > 255)
        {
            errors[nameof(ProviderInput.DisplayName)] = "A name is required (at most 255 characters).";
        }

        if (Clean(input.Manufacturer).Length > 255)
        {
            errors[nameof(ProviderInput.Manufacturer)] = "The manufacturer can be at most 255 characters.";
        }

        var website = Clean(input.WebsiteUrl);
        if (website.Length > 255 || (website.Length > 0 && !(Uri.TryCreate(website, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")))
        {
            errors[nameof(ProviderInput.WebsiteUrl)] = "A web address starting with http:// or https://, or empty.";
        }

        var wanted = WantedTransports(input);
        var unknown = wanted.FirstOrDefault(t => !Enum.TryParse<AttendanceTransport>(t, out var transport) || !Enum.IsDefined(transport));
        if (unknown is not null)
        {
            errors[nameof(ProviderInput.Transports)] = $"'{unknown}' is not a transport. Choose from: {string.Join(", ", Enum.GetNames<AttendanceTransport>())}.";
        }
        else if (wanted.Count > 0)
        {
            var definitions = await _db.AttendanceTransportDefinitions.AsNoTracking().Where(t => wanted.Contains(t.TransportKey)).ToListAsync(cancellationToken);
            if (definitions.Count != wanted.Count)
            {
                errors[nameof(ProviderInput.Transports)] = "A transport you chose has no definition in this tenant.";
            }
            else if (definitions.FirstOrDefault(t => !t.IsActive && !currentTransportIds.Contains(t.Id)) is { } retired)
            {
                // Like a retired file format: a provider may keep a transport that has since been switched off, but can't be given it.
                errors[nameof(ProviderInput.Transports)] = $"{retired.DisplayName} is switched off, so it can't be added to a provider.";
            }
        }

        // A provider may keep a format that has since been retired; a new choice must be an active one.
        if (input.DefaultFileFormatId is { } formatId
            && !await _db.AttendanceFileFormats.AnyAsync(f => f.Id == formatId && (f.IsActive || f.Id == currentFormatId), cancellationToken))
        {
            errors[nameof(ProviderInput.DefaultFileFormatId)] = "The selected file format does not exist or is not active.";
        }

        return errors;
    }
}
