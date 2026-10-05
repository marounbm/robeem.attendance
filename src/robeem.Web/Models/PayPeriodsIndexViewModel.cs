using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class PayPeriodsIndexViewModel
{
    [BindNever]
    public IReadOnlyList<PayPeriodItem> Periods { get; set; } = Array.Empty<PayPeriodItem>();

    /// <summary>Gets or sets the new period being entered; it starts the day after the latest period ends.</summary>
    public PayPeriodInput Input { get; set; } = new();
}
