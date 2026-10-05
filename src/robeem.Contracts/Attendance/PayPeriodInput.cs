using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

public sealed class PayPeriodInput
{
    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "First day")]
    public DateTime StartDate { get; set; }

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Last day (inclusive)")]
    public DateTime EndDate { get; set; }
}
