using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

/// <summary>What an integration administrator sets when creating or editing an integration.</summary>
public sealed class IntegrationInput
{
    [Required]
    [Display(Name = "Provider")]
    public int ProviderId { get; set; }

    [Required]
    [StringLength(255)]
    [Display(Name = "Name")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name under which the connection secret is kept in configuration. A
    /// reference, never the secret: it is validated to look like a name.
    /// </summary>
    [StringLength(255)]
    [Display(Name = "Secret reference")]
    public string? ConnectionConfigRef { get; set; }

    /// <summary>Gets or sets how the integration receives attendance: an <c>AttendanceTransport</c> name the provider supports, e.g. FileUpload (manual upload), Webhook or RestApi.</summary>
    [Required]
    [StringLength(32)]
    [Display(Name = "Receives attendance by")]
    public string Transport { get; set; } = "FileUpload";

    /// <summary>Gets or sets the custom file format uploads are read by; null for the provider's own layout. Only for FileUpload.</summary>
    [Display(Name = "File format")]
    public int? FileFormatId { get; set; }

    /// <summary>Gets or sets the device (one of this integration's) every punch of an uploaded file is attributed to; null to look up the file's own device ids. Only for FileUpload, and only once the device exists.</summary>
    [Display(Name = "Device for uploaded files")]
    public int? DefaultDeviceId { get; set; }

    /// <summary>Gets or sets the IANA time zone used for punches that carry no UTC offset when their device is not registered, e.g. Asia/Beirut. Empty: such punches fail.</summary>
    [StringLength(64)]
    [Display(Name = "Time zone")]
    public string? TimeZoneId { get; set; }

    [StringLength(100)]
    [Display(Name = "Polling schedule (cron)")]
    public string? PollingScheduleCron { get; set; }

    /// <summary>Gets or sets the status: Active or Paused (Failed is set by the system and cleared by choosing Active).</summary>
    public string Status { get; set; } = "Active";
}
