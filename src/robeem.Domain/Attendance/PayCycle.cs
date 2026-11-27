using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// How often an employee is paid: their pay period follows from it. One per employee, plus one tenant-wide default
    /// (<see cref="EmployeeId"/> null) for employees with none of their own. The periods themselves are computed from this, never stored.
    /// </summary>
    [Serializable]
    [Table(nameof(PayCycle), Schema = nameof(SmartEnum.Module.Attendance))]
    public class PayCycle : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the employee this cycle is for; null for the tenant-wide default.</summary>
        public int? EmployeeId { get; set; }

        public Profile? Employee { get; set; }

        public PayCycleFrequency Frequency { get; set; } = PayCycleFrequency.Monthly;

        /// <summary>Gets or sets the first day of a period, for a weekly or two-weekly cycle (a date, without a time). Later periods follow every 7 or 14 days.</summary>
        public DateTime AnchorDate { get; set; } = new DateTime(2000, 1, 3);

        /// <summary>Gets or sets the day of the month a monthly period starts on, from 1 to 28 (so every month has it).</summary>
        public int StartDay { get; set; } = 1;

        #region not mapped
        [NotMapped]
        public override string Title => $"{Frequency}{(EmployeeId is null ? " (default)" : string.Empty)}";
        #endregion
    }
}
