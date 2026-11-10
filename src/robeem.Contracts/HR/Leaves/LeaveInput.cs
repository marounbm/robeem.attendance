using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.HR.Leaves;

/// <summary>Fields HR sets when adding or editing an employee's leave.</summary>
public sealed class LeaveInput
{
    [Display(Name = "Employee")]
    public int EmployeeId { get; set; }

    /// <summary>Gets or sets the type by name: Annual, Sick, Unpaid or Other.</summary>
    [Required]
    [Display(Name = "Type")]
    public string LeaveType { get; set; } = "Annual";

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "First day")]
    public DateTime StartDate { get; set; } = DateTime.Today;

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Last day")]
    public DateTime EndDate { get; set; } = DateTime.Today;

    /// <summary>Gets or sets a value indicating whether the leave is only part of one day.</summary>
    [Display(Name = "Part of a day")]
    public bool IsPartDay { get; set; }

    /// <summary>Gets or sets the time a part-day leave starts, "HH:mm".</summary>
    [StringLength(5)]
    [Display(Name = "From")]
    public string? StartTime { get; set; }

    /// <summary>Gets or sets the time a part-day leave ends, "HH:mm"; earlier than the start means the next day.</summary>
    [StringLength(5)]
    [Display(Name = "To")]
    public string? EndTime { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
