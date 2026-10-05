using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

public sealed class DeviceInput
{
    [Required]
    [StringLength(100)]
    [Display(Name = "Device id (as the vendor sends it)")]
    public string ExternalDeviceId { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Type")]
    public string DeviceType { get; set; } = "Biometric";

    /// <summary>Gets or sets the IANA time zone of the device's clock: wall-clock punches are read in it.</summary>
    [Required]
    [StringLength(64)]
    [Display(Name = "Time zone")]
    public string TimeZoneId { get; set; } = "UTC";
}
