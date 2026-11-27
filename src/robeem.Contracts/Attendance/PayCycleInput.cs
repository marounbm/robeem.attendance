using System.ComponentModel.DataAnnotations;

namespace robeem.Contracts.Attendance;

/// <summary>How often payroll pays an employee (or, for the default, everyone who has no cycle of their own).</summary>
public sealed class PayCycleInput
{
    /// <summary>The frequencies, as <c>PayCycleFrequency</c> names them.</summary>
    public static readonly IReadOnlyList<string> Frequencies = ["Monthly", "SemiMonthly", "EveryTwoWeeks", "Weekly", "EveryFourWeeks"];

    [Required]
    [Display(Name = "Paid")]
    public string Frequency { get; set; } = "Monthly";

    /// <summary>Gets or sets the first day of a period, for a weekly, two-weekly or four-weekly cycle. Later periods follow every 7, 14 or 28 days.</summary>
    [DataType(DataType.Date)]
    [Display(Name = "A first day of a period")]
    public DateTime AnchorDate { get; set; } = new DateTime(2000, 1, 3);

    /// <summary>
    /// Gets or sets the day of the month a monthly period starts on, 1 to 28; for a twice-a-month cycle, the day its first period starts on, 1 to 13
    /// (its second period starts 15 days later).
    /// </summary>
    [Display(Name = "First period starts on day")]
    public int StartDay { get; set; } = 1;
}
