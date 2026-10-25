using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

/// <summary>What an integration administrator sets when defining an attendance vendor: the company, nothing about how its data arrives (that is the connector).</summary>
public sealed class VendorInput
{
    /// <summary>Gets or sets the stable key. Can't be changed once saved: raw punches store it as their source system.</summary>
    [Required]
    [StringLength(100)]
    [RegularExpression("^[a-z0-9][a-z0-9._-]*$", ErrorMessage = "Use lower-case letters, digits, '.', '_' and '-'.")]
    [Display(Name = "Key")]
    public string VendorKey { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    [Display(Name = "Name")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(255)]
    [Display(Name = "Manufacturer")]
    public string? Manufacturer { get; set; }

    [StringLength(255)]
    [Display(Name = "Website")]
    public string? WebsiteUrl { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
