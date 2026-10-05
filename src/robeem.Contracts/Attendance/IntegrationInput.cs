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

    [StringLength(100)]
    [Display(Name = "Polling schedule (cron)")]
    public string? PollingScheduleCron { get; set; }

    /// <summary>Gets or sets the status: Active or Paused (Failed is set by the system and cleared by choosing Active).</summary>
    public string Status { get; set; } = "Active";
}
