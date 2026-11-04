using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

/// <summary>
/// What is entered to set a device up from the vendor down: the connector (of a vendor), how the device is reached, the device
/// itself. The integration the device belongs to is found, or created, for the connector; the person never names it.
/// </summary>
public sealed class DeviceSetupInput
{
    /// <summary>Gets or sets the key of the catalog connector the device is read through (<c>ConnectorOption.Key</c>).</summary>
    [Required(ErrorMessage = "Choose the connector.")]
    [StringLength(100)]
    [Display(Name = "Connector")]
    public string ConnectorKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an existing connection of the connector's integration to reach the device through. Null: a connection is made
    /// from the fields below (the integration's first connection when the connector has no integration yet).
    /// </summary>
    [Display(Name = "Connection")]
    public int? ConnectionId { get; set; }

    /// <summary>Gets or sets how the device's data arrives: an <c>AttendanceTransport</c> name the connector supports. Ignored when <see cref="ConnectionId"/> is set.</summary>
    [StringLength(32)]
    [Display(Name = "Receives attendance by")]
    public string Transport { get; set; } = "FileUpload";

    /// <summary>Gets or sets the authentication method: a <c>ConnectionAuthMethod</c> name the transport allows. Empty: the transport's usual one.</summary>
    [StringLength(32)]
    [Display(Name = "Authentication")]
    public string? AuthMethod { get; set; }

    /// <summary>Gets or sets the non-secret settings, by the keys the transport declares.</summary>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>Gets or sets the name the connection's secret is kept under in configuration: a reference, never the secret.</summary>
    [StringLength(255)]
    [Display(Name = "Secret reference")]
    public string? ConnectionConfigRef { get; set; }

    /// <summary>Gets or sets the cron schedule of a pulled transport.</summary>
    [StringLength(100)]
    [Display(Name = "Polling schedule (cron)")]
    public string? PollingScheduleCron { get; set; }

    /// <summary>Gets or sets the name the key that decrypts the connector's files is kept under. Only used when this creates the connector's integration; required then if the payload is encrypted.</summary>
    [StringLength(255)]
    [Display(Name = "Decryption key reference")]
    public string? PayloadKeyRef { get; set; }

    /// <summary>Gets or sets the device.</summary>
    [Required]
    public DeviceInput Device { get; set; } = new();
}
