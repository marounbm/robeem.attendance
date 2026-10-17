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

    /// <summary>Gets or sets the id of one of the active device types.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Choose the device's type.")]
    [Display(Name = "Type")]
    public int DeviceTypeId { get; set; }

    [StringLength(100)]
    [Display(Name = "Model")]
    public string Model { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Software version")]
    public string SoftwareVersion { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "SDK version")]
    public string SdkVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the IANA time zone of the device's clock: wall-clock punches are read in it.</summary>
    [Required]
    [StringLength(64)]
    [Display(Name = "Time zone")]
    public string TimeZoneId { get; set; } = "UTC";
}
