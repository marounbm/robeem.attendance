using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's vendor catalog (<c>api/attendance/vendors</c>).</summary>
public interface IVendorsApiClient
{
    Task<IReadOnlyList<VendorListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Gets a vendor as editable input, or null when it does not exist.</summary>
    Task<VendorInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(VendorInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int id, VendorInput input, CancellationToken cancellationToken);
}
