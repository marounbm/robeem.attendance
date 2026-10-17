using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

/// <summary>What an integration administrator sets when defining a kind of punch device.</summary>
public sealed class DeviceTypeInput
{
    [Required]
    [StringLength(100)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Available for new devices")]
    public bool IsActive { get; set; } = true;
}
