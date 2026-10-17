using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

/// <summary>What an integration administrator sets when defining an attendance provider.</summary>
public sealed class ProviderInput
{
    /// <summary>Gets or sets the stable key. A provider with a code adapter must use the adapter's key; one without is read by file formats only. Can't be changed once saved.</summary>
    [Required]
    [StringLength(100)]
    [RegularExpression("^[a-z0-9][a-z0-9._-]*$", ErrorMessage = "Use lower-case letters, digits, '.', '_' and '-'.")]
    [Display(Name = "Key")]
    public string ProviderKey { get; set; } = string.Empty;

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

    /// <summary>Gets or sets the AttendanceTransport names the provider supports besides file upload, which any provider can be given.</summary>
    [Display(Name = "Transports")]
    public List<string> Transports { get; set; } = [];

    /// <summary>Gets or sets the file format an upload is read by when its integration names none.</summary>
    [Display(Name = "Default file format")]
    public int? DefaultFileFormatId { get; set; }

    [Display(Name = "Sends webhooks")]
    public bool SupportsWebhooks { get; set; }

    [Display(Name = "Supports incremental sync")]
    public bool SupportsIncrementalSync { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
