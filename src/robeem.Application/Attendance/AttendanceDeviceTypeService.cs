using Microsoft.EntityFrameworkCore;
using robeem.Application.Attendance.Catalog;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceDeviceTypeService : IAttendanceDeviceTypeService
{
    private readonly ICatalogDbContext _db;
    private readonly ICatalogReader _catalog;
    private readonly IRobeemDbContext _tenantDb;
    private readonly ICatalogWriteAccess _access;
    private readonly IAttendanceAuditTrail _audit;

    public AttendanceDeviceTypeService(ICatalogDbContext db, ICatalogReader catalog, IRobeemDbContext tenantDb, ICatalogWriteAccess access, IAttendanceAuditTrail audit)
    {
        _db = db;
        _catalog = catalog;
        _tenantDb = tenantDb;
        _access = access;
        _audit = audit;
    }

    public async Task<IReadOnlyList<DeviceTypeItem>> ListAsync(CancellationToken cancellationToken) =>
        await _db.AttendanceDeviceTypes.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new DeviceTypeItem { Id = t.Id, Name = t.Name, IsActive = t.IsActive })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DeviceTypeOption>> ListOptionsAsync(CancellationToken cancellationToken) =>
        (await _catalog.ListDeviceTypesAsync(cancellationToken))
            .Where(t => t.IsActive)
            .Select(t => new DeviceTypeOption { Name = t.Name })
            .ToList();

    public async Task<DeviceTypeInput?> GetInputAsync(int id, CancellationToken cancellationToken) =>
        await _db.AttendanceDeviceTypes.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new DeviceTypeInput { Name = t.Name, IsActive = t.IsActive })
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<SaveResult> CreateAsync(DeviceTypeInput input, CancellationToken cancellationToken)
    {
        _access.EnsureCanWrite();
        var errors = await ValidateAsync(input, null, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var type = new AttendanceDeviceType { Name = input.Name.Trim(), IsActive = input.IsActive };
        _db.AttendanceDeviceTypes.Add(type);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceDeviceType), type.Id, $"Device type '{type.Name}' created.");
        await _tenantDb.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(type.Id);
    }

    public async Task<SaveResult> UpdateAsync(int id, DeviceTypeInput input, CancellationToken cancellationToken)
    {
        _access.EnsureCanWrite();
        var type = await _db.AttendanceDeviceTypes.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (type is null)
        {
            return SaveResult.Missing();
        }

        // Devices refer to their type by this name, in every tenant: it is the key, so it stays. Deactivate a type instead.
        if ((input.Name ?? string.Empty).Trim() != type.Name)
        {
            return SaveResult.Invalid(nameof(DeviceTypeInput.Name), "The name can't be changed: devices refer to the type by it. Switch the type off and add another.");
        }

        var before = $"{type.Name}; {(type.IsActive ? "active" : "inactive")}";
        type.IsActive = input.IsActive;
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceDeviceType), type.Id, $"Device type changed. Before: {before}. After: {type.Name}; {(type.IsActive ? "active" : "inactive")}.");
        await _tenantDb.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(type.Id);
    }

    private async Task<Dictionary<string, string>> ValidateAsync(DeviceTypeInput input, int? existingId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > 100)
        {
            errors[nameof(DeviceTypeInput.Name)] = "A name is required (at most 100 characters).";
        }
        else if (await _db.AttendanceDeviceTypes.AnyAsync(t => t.Name.ToLower() == name.ToLower() && t.Id != existingId, cancellationToken))
        {
            errors[nameof(DeviceTypeInput.Name)] = "Another device type already has this name.";
        }

        return errors;
    }
}
