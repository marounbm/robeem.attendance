using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.HR.Holidays;

/// <summary>Fields HR sets when adding or editing a public holiday.</summary>
public sealed class HolidayInput
{
    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Date")]
    public DateTime Date { get; set; } = DateTime.Today;

    /// <summary>Gets or sets the last day of a holiday of several days (adds a row per day); <c>null</c> for one day. Ignored when editing.</summary>
    [DataType(DataType.Date)]
    [Display(Name = "Last day (for several days)")]
    public DateTime? LastDate { get; set; }

    [Required]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the date follows the moon and may still move.</summary>
    [Display(Name = "To be confirmed")]
    public bool IsProvisional { get; set; }
}
