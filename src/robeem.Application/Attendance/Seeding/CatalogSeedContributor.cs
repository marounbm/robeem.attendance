using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Application.Seeding;
using robeem.DataModel;

namespace robeem.Application.Attendance.Seeding;

/// <summary>
/// Gives the prime tenant's catalog robeem's built-in offering (study §20.1): the transports, the device types, the payload
/// formats of the file-based vendors (generic CSV, ZKTeco, BioStar), the vendors and one connector for each. Adds only what
/// is missing, matching rows by key or name, so an edit made by a platform administrator is never overwritten.
/// </summary>
/// <remarks>
/// Which connector is usable is not decided here: a connector is usable when it and its vendor are active and it has a
/// default payload format or a code adapter (<see cref="ConnectorUsability"/>). Runs once, against the prime tenant's catalog; a
/// tenant database is not seeded with any of this.
/// </remarks>
public sealed class CatalogSeedContributor : ICatalogSeedContributor
{
    /// <summary>Gets the run order.</summary>
    public int Order => 100;

    public async Task SeedAsync(ICatalogDbContext db, CancellationToken cancellationToken)
    {
        var transports = await SeedTransportsAsync(db, cancellationToken);
        await SeedDeviceTypesAsync(db, cancellationToken);
        var createdFormats = await SeedFormatsAsync(db, cancellationToken);
        await SeedVendorsAsync(db, transports, createdFormats, cancellationToken);
    }

    private static async Task<Dictionary<string, AttendanceTransportDefinition>> SeedTransportsAsync(ICatalogDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.AttendanceTransportDefinitions.ToDictionaryAsync(t => t.TransportKey, StringComparer.Ordinal, cancellationToken);
        foreach (var seed in AttendanceSeedData.Transports.Where(t => !rows.ContainsKey(t.Transport.ToString())))
        {
            var row = new AttendanceTransportDefinition
            {
                TransportKey = seed.Transport.ToString(),
                DisplayName = seed.DisplayName,
                IsPull = seed.IsPull,
                UsesPayloadFormat = seed.UsesPayloadFormat,
                IsActive = true,
            };
            db.AttendanceTransportDefinitions.Add(row);
            rows[row.TransportKey] = row;
        }

        return rows;
    }

    private static async Task SeedDeviceTypesAsync(ICatalogDbContext db, CancellationToken cancellationToken)
    {
        var rows = (await db.AttendanceDeviceTypes.ToListAsync(cancellationToken)).ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in AttendanceSeedData.DeviceTypes.Where(n => !rows.ContainsKey(n)))
        {
            db.AttendanceDeviceTypes.Add(new AttendanceDeviceType { Name = name, IsActive = true });
        }
    }

    /// <summary>Adds the built-in file formats that are missing, and returns the ones added by this run.</summary>
    private static async Task<Dictionary<string, AttendancePayloadFormat>> SeedFormatsAsync(ICatalogDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.AttendancePayloadFormats.Select(f => f.Name).ToListAsync(cancellationToken);
        var created = new Dictionary<string, AttendancePayloadFormat>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in AttendanceSeedData.Formats.Where(f => !existing.Contains(f.Name, StringComparer.OrdinalIgnoreCase)))
        {
            var format = new AttendancePayloadFormat
            {
                Name = seed.Name,
                Description = seed.Description,
                PayloadKind = AttendancePayloadKind.Auto,
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
                format.EventCodes.Add(new AttendancePayloadEventCode { Code = code, EventType = type });
            }

            db.AttendancePayloadFormats.Add(format);
            created[seed.Name] = format;
        }

        return created;
    }

    private static async Task SeedVendorsAsync(
        ICatalogDbContext db,
        Dictionary<string, AttendanceTransportDefinition> transports,
        Dictionary<string, AttendancePayloadFormat> createdFormats,
        CancellationToken cancellationToken)
    {
        var vendors = (await db.AttendanceVendors.ToListAsync(cancellationToken)).ToDictionary(v => v.VendorKey, StringComparer.Ordinal);
        var connectors = (await db.AttendanceConnectorDefinitions.Include(c => c.Transports).ToListAsync(cancellationToken)).ToDictionary(c => c.ConnectorKey, StringComparer.Ordinal);
        foreach (var seed in AttendanceSeedData.Vendors)
        {
            if (!vendors.TryGetValue(seed.Key, out var vendor))
            {
                vendor = new AttendanceVendor
                {
                    VendorKey = seed.Key,
                    DisplayName = seed.DisplayName,
                    IsActive = seed.IsActive,
                    Manufacturer = seed.Manufacturer,
                    WebsiteUrl = seed.WebsiteUrl,
                };
                db.AttendanceVendors.Add(vendor);
                vendors[seed.Key] = vendor;
            }

            // The connector of a seeded vendor carries the vendor's key. An existing one is left as the administrator has it.
            if (!connectors.TryGetValue(seed.Key, out var connector))
            {
                connector = new AttendanceConnectorDefinition
                {
                    Vendor = vendor,
                    ConnectorKey = seed.Key,
                    DisplayName = seed.DisplayName,
                    AdapterKey = seed.AdapterKey,
                    SupportsIncrementalSync = seed.SupportsIncrementalSync,
                    IsActive = seed.IsActive,
                };
                foreach (var transport in seed.Transports)
                {
                    connector.Transports.Add(new AttendanceConnectorTransport { Transport = transports[transport.ToString()] });
                }

                db.AttendanceConnectorDefinitions.Add(connector);
                connectors[seed.Key] = connector;
            }

            // A format created by this run is the layout the connector is read by: link it. Later runs leave the link alone.
            if (seed.DefaultFormat is { } formatName
                && createdFormats.TryGetValue(formatName, out var format)
                && connector.DefaultPayloadFormatId is null
                && connector.DefaultPayloadFormat is null)
            {
                connector.DefaultPayloadFormat = format;
            }
        }
    }
}
