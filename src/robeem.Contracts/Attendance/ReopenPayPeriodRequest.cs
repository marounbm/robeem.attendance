using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

/// <summary>Why a closed pay period is being opened again. Required, and kept in the audit log.</summary>
public sealed class ReopenPayPeriodRequest
{
    [Required]
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;
}
