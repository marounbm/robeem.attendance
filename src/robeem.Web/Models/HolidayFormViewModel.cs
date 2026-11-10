using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.HR.Holidays;

namespace robeem.Web.Models;

/// <summary>Editing one holiday.</summary>
public sealed class HolidayFormViewModel
{
    [BindNever]
    public int Id { get; set; }

    public HolidayInput Input { get; set; } = new();
}
