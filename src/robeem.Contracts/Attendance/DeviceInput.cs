using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

public sealed class DeviceInput
{
    /// <summary>What a machine can put on each punch, as <see cref="PunchTypes"/> names it, with the words a form shows.</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> PunchTypeOptions =
    [
        ("InOut", "Check-in and check-out only"),
        ("InOutAndBreak", "Check-in, check-out, break-out and break-in"),
        ("None", "No types (every punch is the same)"),
    ];

    [Required]
    [StringLength(100)]
    [Display(Name = "Device id (as the vendor sends it)")]
    public string ExternalDeviceId { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of one of the active device types of the catalog.</summary>
    [Required(ErrorMessage = "Choose the device's type.")]
    [StringLength(100)]
    [Display(Name = "Type")]
    public string DeviceTypeName { get; set; } = string.Empty;

    /// <summary>Gets or sets the connection the device is reached through: one of the integration's own. Null: the integration's default connection.</summary>
    [Display(Name = "Connection")]
    public int? ConnectionId { get; set; }

    [StringLength(100)]
    [Display(Name = "Model")]
    public string Model { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Software version")]
    public string SoftwareVersion { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "SDK version")]
    public string SdkVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what the machine puts on each punch: <c>InOut</c> (check-in and check-out only), <c>InOutAndBreak</c> (those and break-out and
    /// break-in) or <c>None</c> (no types). Only a machine without break types has a gap between a check-out and a check-in read as a break.
    /// </summary>
    [Required]
    [Display(Name = "What the machine sends")]
    public string PunchTypes { get; set; } = "InOut";

    /// <summary>Gets or sets the IANA time zone of the device's clock: wall-clock punches are read in it.</summary>
    [Required]
    [StringLength(64)]
    [Display(Name = "Time zone")]
    public string TimeZoneId { get; set; } = "UTC";
}
