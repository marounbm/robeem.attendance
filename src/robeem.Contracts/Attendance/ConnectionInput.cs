using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

/// <summary>What an administrator sets for one connection of an integration.</summary>
public sealed class ConnectionInput
{
    [Required]
    [StringLength(255)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the transport: an <c>AttendanceTransport</c> name (FileUpload, Sftp, RestApi, Webhook, Database or DeviceSdk).</summary>
    [Required]
    [StringLength(32)]
    [Display(Name = "Receives attendance by")]
    public string Transport { get; set; } = "FileUpload";

    /// <summary>Gets or sets how the connection is authenticated: a <c>ConnectionAuthMethod</c> name the transport allows.</summary>
    [Required]
    [StringLength(32)]
    [Display(Name = "Authentication")]
    public string AuthMethod { get; set; } = "None";

    /// <summary>Gets or sets the transport's non-secret settings, by the keys <see cref="TransportDescriptorItem.Fields"/> declares.</summary>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>Gets or sets the name the credential is kept under in configuration. A reference, never the credential.</summary>
    [StringLength(255)]
    [Display(Name = "Secret reference")]
    public string? SecretRef { get; set; }

    /// <summary>Gets or sets the cron expression of a pulling transport.</summary>
    [StringLength(100)]
    [Display(Name = "Pull schedule")]
    public string? PollingSchedule { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
