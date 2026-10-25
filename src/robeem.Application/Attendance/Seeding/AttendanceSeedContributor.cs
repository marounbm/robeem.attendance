using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Application.Seeding;
using robeem.DataModel;

namespace robeem.Application.Attendance.Seeding;

/// <summary>
/// Gives a tenant database the attendance catalog and the tenant-wide default attendance policy (study §20.1):
/// the transports, the device types, the payload formats of the file-based vendors (generic CSV, ZKTeco), the
/// vendors and one connector for each. Adds only what is missing, matching rows by key or name, so an edit made by an
/// administrator is never overwritten.
/// </summary>
/// <remarks>
/// <para>Which connector is usable is not decided here: a connector is usable when it and its vendor are active and it has a
/// default payload format or a code adapter (<see cref="ConnectorUsability"/>).</para>
/// <para>It also finishes the move of the catalog into tables, once per row: a vendor's transports and a device's
/// type were stored as text, and are read from it into the new tables (the text is then cleared); the payload
/// formats the built-in vendors used to be read by in code are linked to their connector, and to the integrations
/// of that connector that had no format, the run that creates them.</para>
/// </remarks>
public sealed class AttendanceSeedContributor : ISeedContributor
{
    /// <summary>Gets the run order; after the HR seed.</summary>
    public int Order => 200;

    public async Task SeedAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        var transports = await SeedTransportsAsync(db, cancellationToken);
        await SeedDeviceTypesAsync(db, cancellationToken);
        var createdFormats = await SeedFormatsAsync(db, cancellationToken);
        await SeedVendorsAsync(db, transports, createdFormats, cancellationToken);

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
                UsesPayloadFormat = seed.UsesPayloadFormat,
                IsActive = true,
            };
            db.AttendanceTransportDefinitions.Add(row);
            rows[row.TransportKey] = row;
        }

        return rows;
    }

    private static async Task SeedDeviceTypesAsync(IRobeemDbContext db, CancellationToken cancellationToken)
    {
        var rows = (await db.AttendanceDeviceTypes.ToListAsync(cancellationToken)).ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in AttendanceSeedData.DeviceTypes.Where(n => !rows.ContainsKey(n)))
        {
            db.AttendanceDeviceTypes.Add(new AttendanceDeviceType { Name = name, IsActive = true });
        }
    }

    /// <summary>Adds the built-in file formats that are missing, and returns the ones added by this run.</summary>
    private static async Task<Dictionary<string, AttendancePayloadFormat>> SeedFormatsAsync(IRobeemDbContext db, CancellationToken cancellationToken)
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
        IRobeemDbContext db,
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

            // A format created by this run is the layout the connector used to be read by in code: link it, and the
            // integrations that had no format of their own, so they read as before. Later runs leave the link alone.
            if (seed.DefaultFormat is { } formatName && createdFormats.TryGetValue(formatName, out var format))
            {
                if (connector.DefaultPayloadFormatId is null && connector.DefaultPayloadFormat is null)
                {
                    connector.DefaultPayloadFormat = format;
                }

                if (connector.Id != 0)
                {
                    var unformatted = await db.AttendanceIntegrations
                        .Where(i => i.ConnectorId == connector.Id
                            && !i.Connections.Any(c => c.IsDefault && c.Transport != AttendanceTransport.FileUpload)
                            && i.PayloadFormatId == null)
                        .ToListAsync(cancellationToken);
                    foreach (var integration in unformatted)
                    {
                        integration.PayloadFormat = format;
                    }
                }
            }
        }
    }
}
