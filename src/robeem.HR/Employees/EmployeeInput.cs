using System.ComponentModel.DataAnnotations;

namespace robeem.HR.Employees;

/// <summary>Fields an HR user can set when creating or editing an employee.</summary>
public sealed class EmployeeInput
{
    [Required]
    [StringLength(50)]
    [Display(Name = "Employee number")]
    public string EmployeeNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(255)]
    public string? Email { get; set; }

    [Phone]
    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(100)]
    public string? Department { get; set; }

    [StringLength(100)]
    [Display(Name = "Job title")]
    public string? JobTitle { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Hire date")]
    public DateTime HireDate { get; set; } = DateTime.Today;

    [DataType(DataType.Date)]
    [Display(Name = "Termination date")]
    public DateTime? TerminationDate { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    /// <summary>Gets or sets the time schedule the employee follows; <c>null</c> means not assigned.</summary>
    [Display(Name = "Time schedule")]
    public int? TimeScheduleId { get; set; }
}
