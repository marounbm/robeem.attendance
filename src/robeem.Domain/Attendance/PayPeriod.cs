using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A pay period (study §18.3): the dates payroll pays for, and whether it is closed. Attendance
    /// of a closed period is no longer extracted.
    /// </summary>
    /// <remarks>
    /// No Payroll module exists in this solution, so Attendance holds this minimal concept for now.
    /// A real Payroll module should take it over rather than Attendance owning it long-term.
    /// </remarks>
    [Serializable]
    [Table(nameof(PayPeriod), Schema = nameof(SmartEnum.Module.Attendance))]
    public class PayPeriod : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the first date of the period (a date, without a time).</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Gets or sets the last date of the period, inclusive (a date, without a time).</summary>
        public DateTime EndDate { get; set; }

        public bool IsClosed { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => $"{StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}";
        #endregion
    }
}
