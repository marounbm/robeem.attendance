using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Application.Seeding;
using robeem.DataModel;

namespace robeem.Application.Attendance.Seeding;

/// <summary>
/// Gives a tenant database the attendance catalog and the tenant-wide default attendance policy (study §20.1):
/// the transports, the device types, the file formats of the file-based vendors (generic CSV, ZKTeco) and the
/// providers. Adds only what is missing, matching rows by key or name, so an edit made by an administrator is
/// never overwritten.
/// </summary>
/// <remarks>
/// <para>Which provider is usable is not decided here: a provider is usable when it is active and has a default
/// file format or a code adapter (<see cref="ProviderUsability"/>).</para>
/// <para>It also finishes the move of the catalog into tables, once per row: a provider's transports and a device's
/// type were stored as text, and are read from it into the new tables (the text is then cleared); the file
/// formats the built-in vendors used to be read by in code are linked to their provider, and to the integrations
/// of that provider that had no format, the run that creates them.</para>
/// </remarks>
public sealed class AttendanceSeedContributor : ISeedContributor
{
    /// <summary>Gets the run order; after the HR seed.</summary>
    public int Order => 200;

    public async Task SeedAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        var transports = await SeedTransportsAsync(db, cancellationToken);
        var deviceTypes = await SeedDeviceTypesAsync(db, cancellationToken);
        var createdFormats = await SeedFormatsAsync(db, cancellationToken);
        await SeedProvidersAsync(db, transports, createdFormats, cancellationToken);
        await MoveLegacyTextAsync(db, transports, deviceTypes, cancellationToken);

        // The MVP defaults of study §20.1: no inference of unlabeled punches, no overtime
        // pre-approval, the manager approves corrections. An administrator changes them in place.
        if (!await db.AttendancePolicies.AnyAsync(p => p.TimeScheduleId == null, cancellationToken))
        {
            db.AttendancePolicies.Add(new AttendancePolicy
            {
                Title = "Default attendance policy",
                UnlabeledPunchInterpretationMode = UnlabeledPunchInterpretationMode.Off,
                RequiresOvertimePreApproval = false,
                CorrectionApprovalWorkflow = CorrectionApprovalWorkflow.Manager,
            });
        }
    }

    private static async Task<Dictionary<string, AttendanceTransportDefinition>> SeedTransportsAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.AttendanceTransportDefinitions.ToDictionaryAsync(t => t.TransportKey, StringComparer.Ordinal, cancellationToken);
        foreach (var seed in AttendanceSeedData.Transports.Where(t => !rows.ContainsKey(t.Transport.ToString())))
        {
            var row = new AttendanceTransportDefinition
            {
                TransportKey = seed.Transport.ToString(),
                DisplayName = seed.DisplayName,
                IsPull = seed.IsPull,
                UsesFileFormat = seed.UsesFileFormat,
                IsActive = true,
            };
            db.AttendanceTransportDefinitions.Add(row);
            rows[row.TransportKey] = row;
        }

        return rows;
    }

    private static async Task<Dictionary<string, AttendanceDeviceType>> SeedDeviceTypesAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        var rows = (await db.AttendanceDeviceTypes.ToListAsync(cancellationToken)).ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in AttendanceSeedData.DeviceTypes.Where(n => !rows.ContainsKey(n)))
        {
            var row = new AttendanceDeviceType { Name = name, IsActive = true };
            db.AttendanceDeviceTypes.Add(row);
            rows[name] = row;
        }

        return rows;
    }

    /// <summary>Adds the built-in file formats that are missing, and returns the ones added by this run.</summary>
    private static async Task<Dictionary<string, AttendanceFileFormat>> SeedFormatsAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.AttendanceFileFormats.Select(f => f.Name).ToListAsync(cancellationToken);
        var created = new Dictionary<string, AttendanceFileFormat>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in AttendanceSeedData.Formats.Where(f => !existing.Contains(f.Name, StringComparer.OrdinalIgnoreCase)))
        {
            var format = new AttendanceFileFormat
            {
                Name = seed.Name,
                Description = seed.Description,
                FileKind = AttendanceFileKind.Auto,
                Delimiter = ",",
                HasHeaderRow = true,
                SheetNumber = 1,
                IsActive = true,
                EmployeeColumn = seed.EmployeeColumn,
                TimestampColumn = seed.TimestampColumn,
                TypeColumn = seed.TypeColumn,
                DirectionColumn = seed.DirectionColumn,
                DeviceColumn = seed.DeviceColumn,
                TransactionIdColumn = seed.TransactionIdColumn,
                PayloadColumns = seed.PayloadColumns,
                TimestampFormats = string.Join(";", seed.TimestampFormats),
            };
            foreach (var (code, type) in seed.EventCodes)
            {
                format.EventCodes.Add(new AttendanceFileFormatEventCode { Code = code, EventType = type });
            }

            db.AttendanceFileFormats.Add(format);
            created[seed.Name] = format;
        }

        return created;
    }

    private static async Task SeedProvidersAsync(
        IRobeemDbContext db,
        Dictionary<string, AttendanceTransportDefinition> transports,
        Dictionary<string, AttendanceFileFormat> createdFormats,
        CancellationToken cancellationToken)
    {
        var rows = (await db.AttendanceProviders.Include(p => p.Transports).ToListAsync(cancellationToken)).ToDictionary(p => p.ProviderKey, StringComparer.Ordinal);
        foreach (var seed in AttendanceSeedData.Providers)
        {
            if (!rows.TryGetValue(seed.Key, out var provider))
            {
                provider = new AttendanceProvider
                {
                    ProviderKey = seed.Key,
                    DisplayName = seed.DisplayName,
                    SupportsWebhooks = seed.SupportsWebhooks,
                    SupportsIncrementalSync = seed.SupportsIncrementalSync,
                    IsActive = seed.IsActive,
                    Manufacturer = seed.Manufacturer,
                    WebsiteUrl = seed.WebsiteUrl,
                };
                foreach (var transport in seed.Transports)
                {
                    provider.Transports.Add(new AttendanceProviderTransport { Transport = transports[transport.ToString()] });
                }

                db.AttendanceProviders.Add(provider);
                rows[seed.Key] = provider;
            }

            // A format created by this run is the layout the provider used to be read by in code: link it, and the
            // integrations that had no format of their own, so they read as before. Later runs leave the link alone.
            if (seed.DefaultFormat is { } formatName && createdFormats.TryGetValue(formatName, out var format))
            {
                if (provider.DefaultFileFormatId is null && provider.DefaultFileFormat is null)
                {
                    provider.DefaultFileFormat = format;
                }

                if (provider.Id != 0)
                {
                    var unformatted = await db.AttendanceIntegrations
                        .Where(i => i.ProviderId == provider.Id && i.Transport == AttendanceTransport.FileUpload && i.FileFormatId == null)
                        .ToListAsync(cancellationToken);
                    foreach (var integration in unformatted)
                    {
                        integration.FileFormat = format;
                    }
                }
            }
        }
    }

    /// <summary>Reads a provider's transports and a device's type from the text they were once stored as, then clears the text so an administrator's later edits stand.</summary>
    private static async Task MoveLegacyTextAsync(
        IRobeemDbContext db,
        Dictionary<string, AttendanceTransportDefinition> transports,
        Dictionary<string, AttendanceDeviceType> deviceTypes,
        CancellationToken cancellationToken)
    {
        var providers = await db.AttendanceProviders.Include(p => p.Transports).Where(p => p.LegacySupportedTransports != string.Empty).ToListAsync(cancellationToken);
        foreach (var provider in providers)
        {
            var keys = provider.LegacySupportedTransports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var key in keys.Where(k => !string.Equals(k, nameof(AttendanceTransport.FileUpload), StringComparison.OrdinalIgnoreCase)))
            {
                if (Enum.TryParse<AttendanceTransport>(key, ignoreCase: true, out var transport)
                    && Enum.IsDefined(transport)
                    && transports.TryGetValue(transport.ToString(), out var definition)
                    && !provider.Transports.Any(t => t.Transport == definition || (definition.Id != 0 && t.TransportId == definition.Id)))
                {
                    provider.Transports.Add(new AttendanceProviderTransport { Transport = definition });
                }
            }

            provider.LegacySupportedTransports = string.Empty;
        }

        var devices = await db.AttendanceDevices.Where(d => d.DeviceTypeId == null && d.LegacyDeviceType != string.Empty).ToListAsync(cancellationToken);
        foreach (var device in devices)
        {
            if (!deviceTypes.TryGetValue(device.LegacyDeviceType, out var type))
            {
                type = new AttendanceDeviceType { Name = device.LegacyDeviceType, IsActive = true };
                db.AttendanceDeviceTypes.Add(type);
                deviceTypes[device.LegacyDeviceType] = type;
            }

            device.DeviceType = type;
            device.LegacyDeviceType = string.Empty;
        }
    }
}
