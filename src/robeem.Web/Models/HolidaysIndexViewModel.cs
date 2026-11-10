using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.HR.Holidays;

namespace robeem.Web.Models;

/// <summary>The holidays of one year, the years that have any, and the form that adds one.</summary>
public sealed class HolidaysIndexViewModel
{
    [BindNever]
    public int Year { get; set; }

    [BindNever]
    public IReadOnlyList<int> Years { get; set; } = Array.Empty<int>();

    [BindNever]
    public IReadOnlyList<HolidayItem> Holidays { get; set; } = Array.Empty<HolidayItem>();

    public HolidayInput Input { get; set; } = new();
}
