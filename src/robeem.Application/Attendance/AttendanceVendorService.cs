using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceVendorService : IAttendanceVendorService
{
    private static readonly Regex KeyPattern = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant);

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;

    public AttendanceVendorService(IRobeemDbContext db, IAttendanceAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<VendorListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var vendors = await _db.AttendanceVendors.AsNoTracking().OrderBy(v => v.DisplayName).ToListAsync(cancellationToken);
        var connectors = await _db.AttendanceConnectorDefinitions.AsNoTracking()
            .GroupBy(c => c.VendorId)
            .Select(g => new { VendorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.VendorId, g => g.Count, cancellationToken);
        var integrations = await _db.AttendanceIntegrations.AsNoTracking()
            .GroupBy(i => i.Connector!.VendorId)
            .Select(g => new { VendorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.VendorId, g => g.Count, cancellationToken);

        return vendors.Select(v => new VendorListItem
        {
            Id = v.Id,
            VendorKey = v.VendorKey,
            DisplayName = v.DisplayName,
            Manufacturer = v.Manufacturer,
            WebsiteUrl = v.WebsiteUrl,
            IsActive = v.IsActive,
            ConnectorCount = connectors.GetValueOrDefault(v.Id),
            IntegrationCount = integrations.GetValueOrDefault(v.Id),
        }).ToList();
    }

    public async Task<VendorInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        var vendor = await _db.AttendanceVendors.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        return vendor is null
            ? null
            : new VendorInput
            {
                VendorKey = vendor.VendorKey,
                DisplayName = vendor.DisplayName,
                Manufacturer = vendor.Manufacturer,
                WebsiteUrl = vendor.WebsiteUrl,
                IsActive = vendor.IsActive,
            };
    }

    public async Task<SaveResult> CreateAsync(VendorInput input, CancellationToken cancellationToken)
    {
        var errors = Validate(input, null);
        var key = Clean(input.VendorKey);
        if (key.Length > 0 && !errors.ContainsKey(nameof(VendorInput.VendorKey))
            && await _db.AttendanceVendors.AnyAsync(v => v.VendorKey == key, cancellationToken))
        {
            errors[nameof(VendorInput.VendorKey)] = "Another vendor already has this key.";
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var vendor = new AttendanceVendor { VendorKey = key };
        Apply(input, vendor);
        _db.AttendanceVendors.Add(vendor);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceVendor), vendor.Id, $"Vendor '{vendor.DisplayName}' ({vendor.VendorKey}) created: {Summary(input)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(vendor.Id);
    }

    public async Task<SaveResult> UpdateAsync(int id, VendorInput input, CancellationToken cancellationToken)
    {
        var vendor = await _db.AttendanceVendors.SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vendor is null)
        {
            return SaveResult.Missing();
        }

        var errors = Validate(input, vendor.VendorKey);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = $"{vendor.DisplayName}; {(vendor.IsActive ? "active" : "inactive")}";
        Apply(input, vendor);
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceVendor), vendor.Id, $"Vendor '{vendor.VendorKey}' changed. Before: {before}. After: {Summary(input)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(vendor.Id);
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    private static string Summary(VendorInput input) => $"{Clean(input.DisplayName)}; {(input.IsActive ? "active" : "inactive")}";

    private static void Apply(VendorInput input, AttendanceVendor vendor)
    {
        vendor.DisplayName = Clean(input.DisplayName);
        vendor.Manufacturer = Clean(input.Manufacturer);
        vendor.WebsiteUrl = Clean(input.WebsiteUrl);
        vendor.IsActive = input.IsActive;
    }

    private static Dictionary<string, string> Validate(VendorInput input, string? existingKey)
    {
        var errors = new Dictionary<string, string>();

        var key = Clean(input.VendorKey);
        if (existingKey is not null)
        {
            if (key != existingKey)
            {
                errors[nameof(VendorInput.VendorKey)] = "The key can't be changed: the punches it produced are tied to it.";
            }
        }
        else if (key.Length == 0 || key.Length > 100 || !KeyPattern.IsMatch(key))
        {
            errors[nameof(VendorInput.VendorKey)] = "A key is required: lower-case letters, digits, '.', '_' and '-', at most 100 characters.";
        }

        var name = Clean(input.DisplayName);
        if (name.Length == 0 || name.Length > 255)
        {
            errors[nameof(VendorInput.DisplayName)] = "A name is required (at most 255 characters).";
        }

        if (Clean(input.Manufacturer).Length > 255)
        {
            errors[nameof(VendorInput.Manufacturer)] = "The manufacturer can be at most 255 characters.";
        }

        var website = Clean(input.WebsiteUrl);
        if (website.Length > 255 || (website.Length > 0 && !(Uri.TryCreate(website, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")))
        {
            errors[nameof(VendorInput.WebsiteUrl)] = "A web address starting with http:// or https://, or empty.";
        }

        return errors;
    }
}
