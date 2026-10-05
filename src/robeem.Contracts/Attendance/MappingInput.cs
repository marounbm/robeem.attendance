using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

public sealed class MappingInput
{
    /// <summary>Gets or sets the employee id exactly as the vendor sends it (compared case-sensitively).</summary>
    [Required]
    [StringLength(255)]
    [Display(Name = "Vendor employee id")]
    public string ExternalEmployeeId { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Employee")]
    public int EmployeeId { get; set; }
}
