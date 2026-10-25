using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class VendorsIndexViewModel
{
    public IReadOnlyList<VendorListItem> Vendors { get; init; } = Array.Empty<VendorListItem>();
}
