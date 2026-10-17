using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

/// <summary>What an integration administrator may change about a transport: its label, and whether a new integration can choose it. How it behaves is code.</summary>
public sealed class TransportInput
{
    [Required]
    [StringLength(255)]
    [Display(Name = "Name")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "Available for new integrations")]
    public bool IsActive { get; set; } = true;
}
