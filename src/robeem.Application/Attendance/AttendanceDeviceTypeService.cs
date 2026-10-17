using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceDeviceTypeService : IAttendanceDeviceTypeService
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;

    public AttendanceDeviceTypeService(IRobeemDbContext db, IAttendanceAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<DeviceTypeItem>> ListAsync(CancellationToken cancellationToken) =>
        await _db.AttendanceDeviceTypes.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new DeviceTypeItem { Id = t.Id, Name = t.Name, IsActive = t.IsActive, DeviceCount = _db.AttendanceDevices.Count(d => d.DeviceTypeId == t.Id) })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DeviceTypeOption>> ListOptionsAsync(CancellationToken cancellationToken) =>
        await _db.AttendanceDeviceTypes.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new DeviceTypeOption { Id = t.Id, Name = t.Name })
            .ToListAsync(cancellationToken);

    public async Task<DeviceTypeInput?> GetInputAsync(int id, CancellationToken cancellationToken) =>
        await _db.AttendanceDeviceTypes.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new DeviceTypeInput { Name = t.Name, IsActive = t.IsActive })
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<SaveResult> CreateAsync(DeviceTypeInput input, CancellationToken cancellationToken)
    {
        var errors = await ValidateAsync(input, null, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var type = new AttendanceDeviceType { Name = input.Name.Trim(), IsActive = input.IsActive };
        _db.AttendanceDeviceTypes.Add(type);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceDeviceType), type.Id, $"Device type '{type.Name}' created.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(type.Id);
    }

    public async Task<SaveResult> UpdateAsync(int id, DeviceTypeInput input, CancellationToken cancellationToken)
    {
        var type = await _db.AttendanceDeviceTypes.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (type is null)
        {
            return SaveResult.Missing();
        }

        var errors = await ValidateAsync(input, id, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = $"{type.Name}; {(type.IsActive ? "active" : "inactive")}";
        type.Name = input.Name.Trim();
        type.IsActive = input.IsActive;
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceDeviceType), type.Id, $"Device type changed. Before: {before}. After: {type.Name}; {(type.IsActive ? "active" : "inactive")}.");
        await _db.SaveChangesAsync(cancellationToken);
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
