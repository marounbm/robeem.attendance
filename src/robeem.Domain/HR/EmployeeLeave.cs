using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// Time an employee is away with approval: whole days, or part of one day. HR saves it already
    /// approved; there is no approval step here (approval and balances belong to a leave module).
    /// </summary>
    [Serializable]
    [Table(nameof(EmployeeLeave), Schema = nameof(SmartEnum.Module.HR))]
    public class EmployeeLeave : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int EmployeeId { get; set; }

        public Profile? Employee { get; set; }

        public LeaveType LeaveType { get; set; }

        /// <summary>Gets or sets the first day (a date, without a time).</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Gets or sets the last day, inclusive (a date, without a time). Equal to <see cref="StartDate"/> for a part-day leave.</summary>
        public DateTime EndDate { get; set; }

        /// <summary>Gets or sets a value indicating whether the leave covers only part of <see cref="StartDate"/>.</summary>
        public bool IsPartDay { get; set; }

        /// <summary>Gets or sets when a part-day leave starts, as "HH:mm"; empty for whole days.</summary>
        [MaxLength(5)]
        public string StartTime { get; set; } = string.Empty;

        /// <summary>Gets or sets when a part-day leave ends, as "HH:mm"; an end earlier than the start is the next day. Empty for whole days.</summary>
        [MaxLength(5)]
        public string EndTime { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Notes { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => IsPartDay
            ? $"{LeaveType} — {StartDate:d} {StartTime}–{EndTime}"
            : $"{LeaveType} — {StartDate:d}{(EndDate.Date == StartDate.Date ? string.Empty : $" – {EndDate:d}")}";
        #endregion
    }
}
