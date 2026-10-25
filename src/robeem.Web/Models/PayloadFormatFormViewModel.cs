using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class PayloadFormatFormViewModel
{
    /// <summary>Gets or sets the format id; null while creating.</summary>
    [BindNever]
    public int? Id { get; set; }

    public PayloadFormatInput Input { get; set; } = new();

    /// <summary>Gets or sets the result of trying the unsaved format on a sample file, when the user asked for it.</summary>
    [BindNever]
    public PayloadFormatPreview? Preview { get; set; }
}
