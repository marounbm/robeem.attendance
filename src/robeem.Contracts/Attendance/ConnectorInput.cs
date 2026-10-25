using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

/// <summary>What an integration administrator sets when defining a connector: one software or protocol of a vendor.</summary>
public sealed class ConnectorInput
{
    [Display(Name = "Vendor")]
    public int VendorId { get; set; }

    /// <summary>Gets or sets the stable key of the connector. Can't be changed once saved.</summary>
    [Required]
    [StringLength(100)]
    [RegularExpression("^[a-z0-9][a-z0-9._-]*$", ErrorMessage = "Use lower-case letters, digits, '.', '_' and '-'.")]
    [Display(Name = "Key")]
    public string ConnectorKey { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    [Display(Name = "Name")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(1000)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the key of the code adapter that reads this connector, when one does (a pull or a push). It must be the key an
    /// adapter and its interpreter are registered under. Empty: the connector is read by payload formats alone.
    /// </summary>
    [StringLength(100)]
    [Display(Name = "Adapter key")]
    public string? AdapterKey { get; set; }

    /// <summary>Gets or sets the AttendanceTransport names the connector supports besides file upload, which any connector can be given.</summary>
    [Display(Name = "Transports")]
    public List<string> Transports { get; set; } = [];

    /// <summary>Gets or sets the payload format an upload is read by when its integration names none.</summary>
    [Display(Name = "Default payload format")]
    public int? DefaultPayloadFormatId { get; set; }

    [Display(Name = "Supports incremental sync")]
    public bool SupportsIncrementalSync { get; set; }

    /// <summary>Gets or sets how the payload is encrypted: None, Pgp, Aes or PasswordProtected.</summary>
    [Display(Name = "Payload encryption")]
    public string EncryptionKind { get; set; } = "None";

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
