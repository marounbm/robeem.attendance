using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class VendorFormViewModel
{
    /// <summary>Gets or sets the vendor id; null while creating.</summary>
    [BindNever]
    public int? Id { get; set; }

    public VendorInput Input { get; set; } = new();
}
